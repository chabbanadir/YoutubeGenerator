import urllib.request
import urllib.parse
import json
import os
import sys
import mimetypes

print("🚀 Starting upload for Stacked Valorant Short to Daily Dose Of Valorant channel...")

# 1. Get access token from valorant.json
with open('/home/chabba/YoutubeGenerator/workspace/profiles/kinds/valorant.json') as f:
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
print("✅ Access token retrieved successfully.")

# 2. Prepare Video Metadata
video_path = '/home/chabba/YoutubeGenerator/workspace/jobs/valorant/shorts/normal/short_354.mp4'
thumb_path = '/home/chabba/YoutubeGenerator/workspace/jobs/valorant/shorts/normal/thumb_short_354_stacked.jpg'

file_size = os.path.getsize(video_path)
print(f"📦 Video file size: {file_size / (1024*1024):.2f} MB")

metadata = {
    "snippet": {
        "title": "HOW DID HE HIT THIS?! 😱 @noway4u_sir Insane ACE! 🔥 #Shorts #VALORANT",
        "description": "Watch @noway4u_sir pull off an unreal ACE in Valorant! 🎯🔥\n\nDrop a like and subscribe for daily Valorant highlights!\n\n#Shorts #VALORANT #ValorantClips #Gaming #noway4u_sir",
        "tags": ["Shorts", "VALORANT", "Valorant Clips", "noway4u_sir", "Valorant Highlights", "Gaming", "ACE", "Valorant Plays"],
        "categoryId": "20"
    },
    "status": {
        "privacyStatus": "public",
        "selfDeclaredMadeForKids": False
    }
}

# 3. Initiate Resumable Upload Session
init_url = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status"
init_headers = {
    'Authorization': f'Bearer {access_token}',
    'Content-Type': 'application/json; charset=UTF-8',
    'X-Upload-Content-Length': str(file_size),
    'X-Upload-Content-Type': 'video/mp4'
}

req_init = urllib.request.Request(
    init_url,
    data=json.dumps(metadata).encode('utf-8'),
    headers=init_headers,
    method='POST'
)

with urllib.request.urlopen(req_init) as resp_init:
    upload_url = resp_init.headers.get('Location')

if not upload_url:
    print("❌ Failed to initiate resumable upload session.")
    sys.exit(1)

print(f"📡 Resumable upload session initiated: {upload_url[:60]}...")

# 4. Stream Upload Video Data
with open(video_path, 'rb') as f_vid:
    upload_headers = {
        'Authorization': f'Bearer {access_token}',
        'Content-Type': 'video/mp4',
        'Content-Length': str(file_size)
    }
    req_upload = urllib.request.Request(
        upload_url,
        data=f_vid,
        headers=upload_headers,
        method='PUT'
    )
    with urllib.request.urlopen(req_upload) as resp_upload:
        res_data = json.loads(resp_upload.read().decode())

video_id = res_data.get('id')
print(f"🎉 Short successfully uploaded! Video ID = {video_id}")
print(f"🔗 URL: https://youtube.com/shorts/{video_id}")

# 5. Attach 9:16 Custom Thumbnail
if os.path.exists(thumb_path):
    with open(thumb_path, 'rb') as f_th:
        thumb_data = f_th.read()

    thumb_url = f"https://www.googleapis.com/upload/youtube/v3/thumbnails/set?videoId={video_id}"
    req_th = urllib.request.Request(
        thumb_url,
        data=thumb_data,
        headers={
            'Authorization': f'Bearer {access_token}',
            'Content-Type': 'image/jpeg'
        }
    )
    try:
        with urllib.request.urlopen(req_th) as resp_th:
            print("🖼️ Attached 9:16 thumbnail to Short!")
    except Exception as e_th:
        print(f"⚠️ Could not attach thumbnail: {e_th}")

# 6. Record in bag_shorts.json
bag_shorts_path = '/home/chabba/YoutubeGenerator/workspace/bags/bag_shorts.json'
try:
    with open(bag_shorts_path) as f_bag:
        shorts_list = json.load(f_bag)
    
    # Add new item
    shorts_list.append({
        "ShortId": f"stacked_{video_id}",
        "KindKey": "valorant",
        "SubKindKey": "normal",
        "Streamer": "noway4u_sir",
        "Title": metadata["snippet"]["title"],
        "Description": metadata["snippet"]["description"],
        "Tags": metadata["snippet"]["tags"],
        "FilePath": video_path,
        "ThumbnailPath": thumb_path,
        "DurationSeconds": 41.0,
        "ScheduledPublishTime": "2026-10-11T19:35:00Z",
        "IsUploaded": True,
        "YoutubeVideoId": video_id
    })
    with open(bag_shorts_path, 'w') as f_bag:
        json.dump(shorts_list, f_bag, indent=2)
    print("💾 Saved to bag_shorts.json.")
except Exception as e_bag:
    print("Warning saving bag:", e_bag)

print(f"\n✅ All done! Published at https://youtube.com/shorts/{video_id}")
