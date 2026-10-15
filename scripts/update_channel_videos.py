import urllib.request
import urllib.parse
import json
import subprocess
import os
import sys

# 1. Get access token
profiles_dir = '/home/chabba/YoutubeGenerator/workspace/profiles/kinds'
with open(os.path.join(profiles_dir, 'lol.json')) as f:
    creds = json.load(f)

data = urllib.parse.urlencode({
    'client_id': creds['YoutubeClientId'],
    'client_secret': creds['YoutubeClientSecret'],
    'refresh_token': creds['YoutubeRefreshToken'],
    'grant_type': 'refresh_token'
}).encode()

req = urllib.request.Request('https://oauth2.googleapis.com/token', data=data)
with urllib.request.urlopen(req) as resp:
    token_data = json.loads(resp.read().decode())
access_token = token_data['access_token']
print("✅ OAuth access token acquired.")

# Load bag_youtube_done
bag_path = '/home/chabba/YoutubeGenerator/workspace/bags/bag_youtube_done.json'
with open(bag_path) as f:
    montages = json.load(f)

print(f"Loaded {len(montages)} videos from {bag_path}")

# Video mapping configuration: (VideoId, CreatorCutout, HookText, CleanTitleWord)
configs = [
    # 0: PzAbdMU3uio
    {
        "id": "PzAbdMU3uio",
        "creator": "thebausffs.png",
        "hook": "1000 IQ PLAY?!",
        "clean_title": "🔥 EPIC League of Legends Highlights - Pro Plays & Fails 🎯"
    },
    # 1: r9WTgWve594
    {
        "id": "r9WTgWve594",
        "creator": "default_lol_gamer.png",
        "hook": "HE ACTUALLY DID IT?!",
        "clean_title": "🔥 BEST LoL Moments - Pro & Streamer Highlights ⚡"
    },
    # 2: DxS_S_ZDiI4
    {
        "id": "DxS_S_ZDiI4",
        "creator": "nemesis.png",
        "hook": "HOW IS HE REAL?!",
        "clean_title": "🔥 INSANE LoL Highlights - Epic SoloQ Moments ⚡"
    },
    # 3: 8Em8mYHOEIQ
    {
        "id": "8Em8mYHOEIQ",
        "creator": "faker.png",
        "hook": "CHALLENGER 200 IQ?!",
        "clean_title": "🔥 INSANE League of Legends Plays - High Elo Outplays ⚡"
    },
    # 4: IxhlB5M18so
    {
        "id": "IxhlB5M18so",
        "creator": "thebausffs.png",
        "hook": "CLEANEST OUTPLAY?!",
        "clean_title": "🔥 Insane League of Legends Highlights - Epic SoloQ Plays 🎯"
    },
    # 5: 0sw6aUflwqA
    {
        "id": "0sw6aUflwqA",
        "creator": "loltyler1.png",
        "hook": "WAIT WHAT?!",
        "clean_title": "🔥 EPIC League of Legends Highlights - Best SoloQ Plays ⚡"
    },
    # 6: x7tEF4_tDAM
    {
        "id": "x7tEF4_tDAM",
        "creator": "default_lol_gamer.png",
        "hook": "IS HE SCRIPTING?!",
        "clean_title": "🔥 EPIC League of Legends Plays - High Elo Moments 🎯"
    },
    # 7: 9k1BAZrDABg
    {
        "id": "9k1BAZrDABg",
        "creator": "loltyler1.png",
        "hook": "UNREAL MECHANICS?!",
        "clean_title": "🔥 INSANE League of Legends Moments - Epic Streamer Plays ⚡"
    },
    # 8: n40He63ExKo
    {
        "id": "n40He63ExKo",
        "creator": "faker.png",
        "hook": "HE NEVER MISSES?!",
        "clean_title": "🔥 INSANE League of Legends Moments - Cleanest Outplays ⚡"
    },
    # 9: 5JK8ADj4Vco
    {
        "id": "5JK8ADj4Vco",
        "creator": "thebausffs.png",
        "hook": "1000 IQ PLAY?!",
        "clean_title": "🔥 EPIC League of Legends Moments - Best SoloQ Highlights 🎯"
    },
    # 10: XrQyPzilQWw
    {
        "id": "XrQyPzilQWw",
        "creator": "default_lol_gamer.png",
        "hook": "BRO WHAT?!",
        "clean_title": "🔥 Epic League of Legends Moments - Best LoL Highlights ⚡"
    }
]

font_path = '/app/workspace/ressources/thumbnails/fonts/beaufort_heavy.otf'
creators_base = '/app/workspace/ressources/thumbnails/creators'

for cfg in configs:
    vid = cfg["id"]
    creator_file = cfg["creator"]
    hook = cfg["hook"]
    clean_title = cfg["clean_title"]

    print(f"\n==========================================")
    print(f"🎬 Processing video {vid}...")

    # Fetch current snippet from YouTube
    url_get = f"https://www.googleapis.com/youtube/v3/videos?part=snippet&id={vid}"
    req_get = urllib.request.Request(url_get, headers={'Authorization': f'Bearer {access_token}'})
    try:
        with urllib.request.urlopen(req_get) as resp:
            data_get = json.loads(resp.read().decode())
    except Exception as e:
        print(f"⚠️ Could not fetch video {vid}: {e}")
        continue

    if not data_get.get("items"):
        print(f"⚠️ Video {vid} not found on YouTube.")
        continue

    snippet = data_get["items"][0]["snippet"]
    current_title = snippet["title"]
    category_id = snippet.get("categoryId", "20") # 20 = Gaming

    # Find matching montage item in bag
    montage_item = next((m for m in montages if m.get("MontageId") == vid), None)
    streamer_names = []
    if montage_item:
        for c in montage_item.get("Clips", []):
            s = c.get("Description", "").strip().lstrip("@")
            if s and s not in streamer_names and len(s) < 25:
                streamer_names.append(s)

    # 1. DOWNLOAD ACTION BACKGROUND
    bg_local = f"/tmp/bg_{vid}.jpg"
    downloaded = False
    # Try maxres from YouTube
    for url_candidate in [
        f"https://img.youtube.com/vi/{vid}/maxresdefault.jpg",
        snippet.get("thumbnails", {}).get("maxres", {}).get("url"),
        snippet.get("thumbnails", {}).get("high", {}).get("url"),
        f"https://img.youtube.com/vi/{vid}/hqdefault.jpg"
    ]:
        if not url_candidate:
            continue
        try:
            req_dl = urllib.request.Request(url_candidate, headers={'User-Agent': 'Mozilla/5.0'})
            with urllib.request.urlopen(req_dl) as r_dl:
                with open(bg_local, 'wb') as f_out:
                    f_out.write(r_dl.read())
            if os.path.getsize(bg_local) > 5000:
                downloaded = True
                print(f"  📸 Background acquired from: {url_candidate[:50]}... ({os.path.getsize(bg_local)} bytes)")
                break
        except Exception:
            continue

    if not downloaded:
        print(f"  ⚠️ Could not acquire background for {vid}, skipping thumbnail compositing.")
    else:
        # 2. COMPOSITE THUMBNAIL VIA DOCKER FFMPEG
        subprocess.run(['docker', 'cp', bg_local, f'youtube_generator:/tmp/bg_{vid}.jpg'], check=True)
        creator_path = f"{creators_base}/{creator_file}"
        in_bg = f"/tmp/bg_{vid}.jpg"
        out_cont = f"/tmp/thumb_{vid}.jpg"
        out_local = f"/tmp/thumb_{vid}.jpg"

        filter_graph = (
            "[0:v]scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,eq=contrast=1.15:saturation=1.25,"
            "drawbox=x=0:y=390:w=350:h=330:color=black@0.60:t=fill[bg];"
            f"[1:v]scale=-1:490[creator];"
            f"[bg][creator]overlay=x=W-w:y=H-h[layer];"
            f"[layer]drawtext=fontfile='{font_path}':text='{hook}':fontcolor=0xFFE600:fontsize=96:borderw=10:bordercolor=black:x=60:y=40[out]"
        )

        cmd = [
            'docker', 'exec', 'youtube_generator',
            'ffmpeg', '-y',
            '-i', in_bg,
            '-i', creator_path,
            '-filter_complex', filter_graph,
            '-map', '[out]',
            '-vframes', '1',
            '-q:v', '2',
            out_cont
        ]
        res_ff = subprocess.run(cmd, capture_output=True, text=True)
        if res_ff.returncode == 0:
            subprocess.run(['docker', 'cp', f'youtube_generator:{out_cont}', out_local], check=True)
            print(f"  🎨 Rendered 16:9 thumbnail: {out_local} ({os.path.getsize(out_local)} bytes)")

            # 3. UPLOAD THUMBNAIL TO YOUTUBE
            with open(out_local, 'rb') as f_thumb:
                img_data = f_thumb.read()

            upload_url = f"https://www.googleapis.com/upload/youtube/v3/thumbnails/set?videoId={vid}"
            req_thumb = urllib.request.Request(
                upload_url,
                data=img_data,
                headers={
                    'Authorization': f'Bearer {access_token}',
                    'Content-Type': 'image/jpeg'
                }
            )
            try:
                with urllib.request.urlopen(req_thumb) as resp_th:
                    print(f"  🖼️ Custom Thumbnail successfully attached to {vid}!")
            except urllib.error.HTTPError as e:
                print(f"  ⚠️ Error attaching thumbnail to {vid}: {e.code} {e.read().decode()[:200]}")
        else:
            print(f"  ⚠️ FFmpeg failed: {res_ff.stderr[:200]}")

    # 4. UPDATE VIDEO TITLE, DESCRIPTION, AND TAGS
    desc = snippet.get("description", "")
    # Clean any "normal game" phrases out of description
    desc_clean = desc.replace("normal game highlights", "League of Legends highlights")
    desc_clean = desc_clean.replace("normal games", "high-level League matches")
    desc_clean = desc_clean.replace("normal game moments", "epic League moments")
    desc_clean = desc_clean.replace("normal game clips", "epic League clips")
    desc_clean = desc_clean.replace("normal game action", "epic League action")

    # Build rich tags
    base_tags = [
        "League of Legends", "LoL", "LoL Highlights", "League of Legends Highlights",
        "LoL Clips", "League Highlights", "LoL Montage", "Riot Games",
        "LoL Best Moments", "LoL Outplays", "LoL Funny Moments", "League SoloQ", "High Elo"
    ]
    # Add streamers as tags
    for st in streamer_names[:10]:
        if st not in base_tags:
            base_tags.append(st)

    update_payload = {
        "id": vid,
        "snippet": {
            "title": clean_title,
            "description": desc_clean,
            "tags": base_tags,
            "categoryId": category_id
        }
    }

    url_put = f"https://www.googleapis.com/youtube/v3/videos?part=snippet"
    req_put = urllib.request.Request(
        url_put,
        data=json.dumps(update_payload).encode('utf-8'),
        headers={
            'Authorization': f'Bearer {access_token}',
            'Content-Type': 'application/json'
        },
        method='PUT'
    )

    try:
        with urllib.request.urlopen(req_put) as resp_put:
            res_json = json.loads(resp_put.read().decode())
            print(f"  📝 Video metadata updated! Title: '{clean_title}' | Tags: {len(base_tags)}")
            if montage_item:
                montage_item["GeneratedTitle"] = clean_title
                montage_item["GeneratedDescription"] = desc_clean
                montage_item["GeneratedTags"] = base_tags
    except urllib.error.HTTPError as e:
        print(f"  ⚠️ Error updating video metadata for {vid}: {e.code} {e.read().decode()[:200]}")

# Save updated bag_youtube_done.json
with open(bag_path, 'w') as f:
    json.dump(montages, f, indent=2)

print("\n🎉 All 11 videos processed and updated with custom thumbnails, clean titles, and rich tags!")
