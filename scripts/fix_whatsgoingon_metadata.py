#!/usr/bin/env python3
import json
import os
import sys

BAG_PATH = os.environ.get("BAG_SHORTS_PATH", "/home/chabba/YoutubeGenerator/workspace/bags/bag_shorts.json")

CURATED_METADATA = [
    (
        "KAI CENAT ACTUALLY LOST HIS MIND 😭 @kaicenat #Shorts #JustChatting",
        "Kai Cenat with zero hesitation! 😂\nClip featuring @kaicenat on Twitch.\nWould you do this? Drop your thoughts below! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #FunnyMoments"
    ),
    (
        "HE REALLY DID THIS ON STREAM 💀 @kaicenat #Shorts #JustChatting",
        "No way this actually happened live on stream! 😱\nClip featuring @kaicenat on Twitch.\nDid you see this live? Rate in the comments! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #Twitch #ViralMoments"
    ),
    (
        "BRO WAS NOT READY FOR WHAT HAPPENED 💀 @kaicenat #Shorts #JustChatting",
        "Kai Cenat's reaction is absolutely priceless! 😭\nClip featuring @kaicenat on Twitch.\nComment your favorite part below! 👇\nSubscribe for more daily Twitch clips!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #Trending"
    ),
    (
        "KAI CENAT HAD ZERO WORDS FOR THIS 😭🔥 @kaicenat #Shorts #JustChatting",
        "The stream energy was completely unhinged today! 😂\nClip featuring @kaicenat on Twitch.\nIs Kai the funniest streamer right now? Let us know below! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #StreamerHighlights #Viral"
    ),
    (
        "HE WAS NOT EXPECTING THIS TO HAPPEN 😱 @kaicenat #Shorts #JustChatting",
        "Kai Cenat couldn't believe what just went down! 💀\nClip featuring @kaicenat on Twitch.\nDid you see this coming? Drop a comment below! 👇\nSubscribe for daily clips!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #Funny"
    ),
    (
        "KAI CENAT CANNOT BE STOPPED 💀🔥 @kaicenat #Shorts #JustChatting",
        "Pure comedy on Kai Cenat's stream! 🤣\nClip featuring @kaicenat on Twitch.\nRate this moment from 1 to 10 below! 👇\nSubscribe for daily Twitch highlights!\n\n#Shorts #JustChatting #KaiCenat #Viral #Trending"
    ),
    (
        "THIS MIGHT BE HIS WILDEST STREAM MOMENT 😭 @kaicenat #Shorts #JustChatting",
        "Kai Cenat takes stream moments to a whole different level! 💥\nClip featuring @kaicenat on Twitch.\nShare your reaction in comments! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #StreamerMoments #FunnyMoments"
    ),
    (
        "BRO CAN NEVER CATCH A BREAK 💀😭 @kaicenat #Shorts #JustChatting",
        "Another legendary stream clip from Kai Cenat! 😂\nClip featuring @kaicenat on Twitch.\nDrop a like if this made you laugh! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #Comedy"
    ),
    (
        "KAI CENAT THOUGHT NOBODY WAS WATCHING 💀 @kaicenat #Shorts #JustChatting",
        "Caught 4k in full HD on stream! 😭\nClip featuring @kaicenat on Twitch.\nHow would you react in this situation? 👇\nSubscribe for daily stream clips!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #StreamerMoments"
    ),
    (
        "HE DID NOT JUST SAY THAT ON LIVE 😭 @kaicenat #Shorts #JustChatting",
        "Out of context Kai Cenat is undefeated! 💀\nClip featuring @kaicenat on Twitch.\nDid he really just say that? Leave a comment! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #FunnyMoments #Trending"
    ),
    (
        "KAI CENAT'S STREAM REACHED PEAK CHAOS 💀🔥 @kaicenat #Shorts #JustChatting",
        "Absolute chaos took over the room! 😂\nClip featuring @kaicenat on Twitch.\nComment a W in chat if you love Kai's energy! 👇\nSubscribe for more highlights!\n\n#Shorts #JustChatting #KaiCenat #ViralMoments #Twitch"
    ),
    (
        "THE ENTIRE CHAT WAS SPAMMING W 😱 @kaicenat #Shorts #JustChatting",
        "Kai Cenat had everyone in pure disbelief! 💥\nClip featuring @kaicenat on Twitch.\nWhat was your reaction to this? Let us know below! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #Trending"
    ),
    (
        "KAI CENAT STARTED CRASHING OUT 😭💀 @kaicenat #Shorts #JustChatting",
        "Nobody crashes out quite like Kai Cenat! 🤣\nClip featuring @kaicenat on Twitch.\nRate the crash out from 1-10! 👇\nSubscribe for daily stream clips!\n\n#Shorts #JustChatting #KaiCenat #Funny #StreamerHighlights"
    ),
    (
        "NO WAY HE GOT AWAY WITH THIS 💀 @kaicenat #Shorts #JustChatting",
        "How did he actually pull this off on stream?! 😱\nClip featuring @kaicenat on Twitch.\nDrop your thoughts in the comments! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #Viral"
    ),
    (
        "KAI CENAT COULD NOT HOLD IT IN 🤣 @kaicenat #Shorts #JustChatting",
        "Uncontrollable laughter broke out during the stream! 😂\nClip featuring @kaicenat on Twitch.\nTry not to laugh challenge impossible edition! 👇\nSubscribe for daily stream clips!\n\n#Shorts #JustChatting #KaiCenat #FunnyMoments #Comedy"
    ),
    (
        "HE REGRETTED THIS DECISION INSTANTLY 😭 @kaicenat #Shorts #JustChatting",
        "Instant regret captured live on stream! 💀\nClip featuring @kaicenat on Twitch.\nWould you have done the same? Comment below! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #Twitch #Trending"
    ),
    (
        "KAI CENAT GAVE HIS STREAM 1000% ENERGY 🔥 @kaicenat #Shorts #JustChatting",
        "The highest energy stream moment you'll see all week! 💥\nClip featuring @kaicenat on Twitch.\nComment your favorite streamer below! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #Viral #StreamerMoments"
    ),
    (
        "THIS IS WHY KAI CENAT IS NUMBER ONE 💀 @kaicenat #Shorts #JustChatting",
        "Unmatched entertainment value on stream! 😭\nClip featuring @kaicenat on Twitch.\nDo you think Kai is the GOAT? Let us know! 👇\nSubscribe for daily Twitch clips!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #FunnyMoments"
    ),
    (
        "BRO COULD NOT BELIEVE HIS EYES 😱 @kaicenat #Shorts #JustChatting",
        "The shock on Kai Cenat's face says it all! 💀\nClip featuring @kaicenat on Twitch.\nWhat would you do here? Drop a comment! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #Funny #Trending"
    ),
    (
        "KAI CENAT'S REACTION IS COMPLETELY PRICELESS 🤣 @kaicenat #Shorts #JustChatting",
        "You cannot script moments like this! 😂\nClip featuring @kaicenat on Twitch.\nShare this with a friend who loves Kai! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #StreamerHighlights #Comedy"
    ),
    (
        "HE CAUGHT EVERYONE OFF GUARD 💀 @kaicenat #Shorts #JustChatting",
        "Nobody saw this plot twist coming! 😱\nClip featuring @kaicenat on Twitch.\nRate this surprise from 1-10! 👇\nSubscribe for daily Twitch clips!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #ViralMoments"
    ),
    (
        "KAI CENAT WAS FIGHTING FOR HIS LIFE 😭 @kaicenat #Shorts #JustChatting",
        "Kai Cenat was in complete survival mode here! 💀\nClip featuring @kaicenat on Twitch.\nHow did he survive this? Comment below! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #FunnyMoments #Trending"
    ),
    (
        "THE FUNNIEST 30 SECONDS ON TWITCH 💀🔥 @kaicenat #Shorts #JustChatting",
        "Non-stop laughs from beginning to end! 🤣\nClip featuring @kaicenat on Twitch.\nComment your favorite part below! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #Comedy #Viral"
    ),
    (
        "KAI CENAT DID NOT HESITATE FOR A SECOND 😱 @kaicenat #Shorts #JustChatting",
        "Full confidence and pure stream gold! 💥\nClip featuring @kaicenat on Twitch.\nWould you dare to do this? Let us know! 👇\nSubscribe for daily clips!\n\n#Shorts #JustChatting #KaiCenat #Twitch #StreamerMoments"
    ),
    (
        "BRO WAS ON A COMPLETELY DIFFERENT PLANET 😭 @kaicenat #Shorts #JustChatting",
        "Kai Cenat was operating on another level of reality! 💀\nClip featuring @kaicenat on Twitch.\nDrop an emoji that describes your reaction! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #Funny #TwitchClips"
    ),
    (
        "KAI CENAT WENT ABSOLUTELY UNHINGED 💀 @kaicenat #Shorts #JustChatting",
        "Zero filters and 100% comedy! 😂\nClip featuring @kaicenat on Twitch.\nIs this his best moment this month? Comment below! 👇\nSubscribe for daily stream clips!\n\n#Shorts #JustChatting #KaiCenat #ViralMoments #Trending"
    ),
    (
        "YOU WILL NEVER GUESS WHAT HE DID 😱🔥 @kaicenat #Shorts #JustChatting",
        "Watch till the end because you won't believe this! 😭\nClip featuring @kaicenat on Twitch.\nDid you expect that outcome? Let us know! 👇\nSubscribe for daily highlights!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #FunnyMoments"
    ),
    (
        "KAI CENAT PROVED HE HAS ZERO CHILL 😭 @kaicenat #Shorts #JustChatting",
        "No chill detected in Kai's stream today! 💀\nClip featuring @kaicenat on Twitch.\nDrop your thoughts in the comments! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #Comedy #Viral"
    ),
    (
        "HE WAS SPEECHLESS AFTER THIS HAPPENED 💀 @kaicenat #Shorts #JustChatting",
        "Kai Cenat couldn't utter a single coherent word! 🤣\nClip featuring @kaicenat on Twitch.\nWhat would you say in this situation? Comment below! 👇\nSubscribe for daily clips!\n\n#Shorts #JustChatting #KaiCenat #StreamerHighlights #Trending"
    ),
    (
        "KAI CENAT CREATED PURE GOLD HERE 🤣 @kaicenat #Shorts #JustChatting",
        "Peak streaming content right here! 😂\nClip featuring @kaicenat on Twitch.\nRate this clip 1 to 10 below! 👇\nSubscribe for daily Twitch highlights!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #Funny"
    ),
    (
        "BRO HAD TO DOUBLE CHECK HIS SCREEN 😱 @kaicenat #Shorts #JustChatting",
        "Kai Cenat had to do a double take right on stream! 💀\nClip featuring @kaicenat on Twitch.\nDid you see what just happened? Drop a comment! 👇\nSubscribe for daily Twitch Just Chatting highlights!\n\n#Shorts #JustChatting #KaiCenat #Viral #StreamerMoments"
    ),
    (
        "THE ULTIMATE KAI CENAT STREAM MOMENT 💀🔥 @kaicenat #Shorts #JustChatting",
        "This defines what makes Kai Cenat's streams legendary! 💥\nClip featuring @kaicenat on Twitch.\nComment 'W' if you agree! 👇\nSubscribe for daily Twitch clips!\n\n#Shorts #JustChatting #KaiCenat #TwitchClips #ViralMoments"
    )
]

DEFAULT_TAGS = [
    "Shorts",
    "Kai Cenat",
    "Kai Cenat Clips",
    "Just Chatting",
    "Twitch Highlights",
    "Stream Highlights",
    "Funny Moments",
    "Twitch Clips",
    "Viral",
    "Trending",
    "Kai Cenat Live",
    "Twitch Reactions"
]

def main():
    if not os.path.exists(BAG_PATH):
        print(f"Error: {BAG_PATH} does not exist!")
        sys.exit(1)

    with open(BAG_PATH, "r", encoding="utf-8") as f:
        shorts = json.load(f)

    target_shorts = [
        s for s in shorts 
        if "whats" in (s.get("KindKey") or "").lower() and not s.get("IsUploaded")
    ]
    print(f"Found {len(target_shorts)} pending shorts for 'whats_going_on'.")

    for idx, s in enumerate(target_shorts):
        template_idx = idx % len(CURATED_METADATA)
        title, desc = CURATED_METADATA[template_idx]
        
        s["Title"] = title
        s["Description"] = desc
        s["Tags"] = list(DEFAULT_TAGS)
        s["KindKey"] = "whats_going_on"
        s["SubKindKey"] = "just_chatting"
        print(f"[{idx + 1}/{len(target_shorts)}] -> {title}")

    # Write back to bag_shorts.json
    tmp_path = f"{BAG_PATH}.tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(shorts, f, indent=2, ensure_ascii=False)
    os.replace(tmp_path, BAG_PATH)

    print(f"\n🎉 Successfully updated all {len(target_shorts)} shorts with unique viral Just Chatting metadata!")

if __name__ == "__main__":
    main()
