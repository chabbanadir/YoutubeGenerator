#!/usr/bin/env python3
"""
WebcamDetector.py
Robust detector for streamer facecams in LoL & Valorant gameplay clips.
Uses known streamer presets with ROI validation, game-specific HUD exclusion,
multi-cascade face detection, and skin-tone verification to eliminate false positives
(e.g., champion portraits, donation goals, game UI).

Outputs JSON: {"has_facecam": bool, "x": int, "y": int, "w": int, "h": int}
"""

import sys
import json
import os

# Accurate 16:9 streamer webcam coordinates on 1920x1080 canvas
STREAMER_PRESETS = {
    # Valorant creators
    "noway4u": {"has_facecam": True, "x": 95, "y": 505, "w": 430, "h": 265},
    "tarik": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 265},
    "tenz": {"has_facecam": True, "x": 1440, "y": 120, "w": 460, "h": 270},
    "s0m": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 265},
    "kyedae": {"has_facecam": True, "x": 40, "y": 160, "w": 450, "h": 270},
    "subroza": {"has_facecam": True, "x": 30, "y": 680, "w": 440, "h": 265},
    "shanks": {"has_facecam": True, "x": 30, "y": 680, "w": 440, "h": 265},
    "fns": {"has_facecam": True, "x": 30, "y": 680, "w": 440, "h": 265},
    "hiko": {"has_facecam": True, "x": 30, "y": 140, "w": 440, "h": 265},
    "minibo0": {"has_facecam": True, "x": 15, "y": 30, "w": 420, "h": 265},
    "zikz": {"has_facecam": True, "x": 1540, "y": 480, "w": 380, "h": 265},
    "shahzam": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 265},
    "aspas": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 265},
    "boaster": {"has_facecam": True, "x": 30, "y": 120, "w": 440, "h": 265},
    "sinatraa": {"has_facecam": True, "x": 10, "y": 60, "w": 460, "h": 270},

    # LoL creators
    "velja": {"has_facecam": True, "x": 1205, "y": 767, "w": 430, "h": 268},
    "thebausffs": {"has_facecam": True, "x": 10, "y": 580, "w": 440, "h": 265},
    "baus": {"has_facecam": True, "x": 10, "y": 580, "w": 440, "h": 265},
    "nemesis": {"has_facecam": True, "x": 1450, "y": 680, "w": 440, "h": 265},
    "kesha": {"has_facecam": True, "x": 10, "y": 10, "w": 440, "h": 265},
    "tyler1": {"has_facecam": True, "x": 10, "y": 10, "w": 440, "h": 270},
    "nattynatt": {"has_facecam": True, "x": 1450, "y": 700, "w": 440, "h": 265},
    "lltrigger": {"has_facecam": True, "x": 10, "y": 680, "w": 440, "h": 265},
    "faker": {"has_facecam": True, "x": 1450, "y": 100, "w": 440, "h": 265},
    "solorenekton": {"has_facecam": True, "x": 10, "y": 680, "w": 440, "h": 265},
    "tarzaned": {"has_facecam": True, "x": 10, "y": 680, "w": 440, "h": 265},
    "dantes": {"has_facecam": True, "x": 10, "y": 680, "w": 440, "h": 265},
    "drututt": {"has_facecam": True, "x": 1450, "y": 680, "w": 440, "h": 265},
    "yamato": {"has_facecam": True, "x": 1450, "y": 680, "w": 440, "h": 265},
    "hylissang": {"has_facecam": True, "x": 1210, "y": 790, "w": 420, "h": 280},
    "hylissanglol": {"has_facecam": True, "x": 1210, "y": 790, "w": 420, "h": 280},
    "hien": {"has_facecam": True, "x": 30, "y": 724, "w": 430, "h": 265},
}

CASCADE_CANDIDATE_PATHS = [
    "/usr/share/opencv4/haarcascades/haarcascade_frontalface_alt2.xml",
    "/usr/share/opencv4/haarcascades/haarcascade_frontalface_alt.xml",
    "/usr/share/opencv4/haarcascades/haarcascade_frontalface_default.xml",
    "/usr/share/opencv4/haarcascades/haarcascade_profileface.xml",
    "/usr/share/opencv/haarcascades/haarcascade_frontalface_alt2.xml",
    "/usr/share/opencv/haarcascades/haarcascade_frontalface_alt.xml",
    "/usr/share/opencv/haarcascades/haarcascade_frontalface_default.xml",
    "/usr/share/opencv/haarcascades/haarcascade_profileface.xml",
]

EYE_CASCADE_CANDIDATE_PATHS = [
    "/usr/share/opencv4/haarcascades/haarcascade_eye.xml",
    "/usr/share/opencv4/haarcascades/haarcascade_eye_tree_eyeglasses.xml",
    "/usr/share/opencv/haarcascades/haarcascade_eye.xml",
    "/usr/share/opencv/haarcascades/haarcascade_eye_tree_eyeglasses.xml",
]

def has_skin_tones(bgr_crop):
    """
    Check if a candidate crop has realistic human skin tones.
    Combines YCrCb and HSV color spaces to strictly eliminate false triggers
    on text, donation goals, in-game rocks, grass, and HUD graphics.
    """
    if bgr_crop is None or bgr_crop.size == 0:
        return False
    try:
        import cv2
        import numpy as np

        # 1. YCrCb skin threshold (standard human skin gamut)
        ycrcb = cv2.cvtColor(bgr_crop, cv2.COLOR_BGR2YCrCb)
        lower_ycrcb = np.array([0, 135, 85], dtype=np.uint8)
        upper_ycrcb = np.array([255, 175, 135], dtype=np.uint8)
        mask_ycrcb = cv2.inRange(ycrcb, lower_ycrcb, upper_ycrcb)

        # 2. HSV skin threshold (excludes green grass, cyan spells, pure white text)
        hsv = cv2.cvtColor(bgr_crop, cv2.COLOR_BGR2HSV)
        lower_hsv1 = np.array([0, 25, 50], dtype=np.uint8)
        upper_hsv1 = np.array([25, 210, 255], dtype=np.uint8)
        mask_hsv1 = cv2.inRange(hsv, lower_hsv1, upper_hsv1)
        lower_hsv2 = np.array([165, 25, 50], dtype=np.uint8)
        upper_hsv2 = np.array([180, 210, 255], dtype=np.uint8)
        mask_hsv2 = cv2.inRange(hsv, lower_hsv2, upper_hsv2)
        mask_hsv = cv2.bitwise_or(mask_hsv1, mask_hsv2)

        # Combined intersection of both color spaces
        combined_mask = cv2.bitwise_and(mask_ycrcb, mask_hsv)

        total_pixels = bgr_crop.shape[0] * bgr_crop.shape[1]
        skin_pixels = cv2.countNonZero(combined_mask)
        skin_ratio = skin_pixels / float(max(1, total_pixels))

        # Real streamer face crops have at least 18% skin tone pixels
        return skin_ratio >= 0.18
    except Exception:
        return False

def verify_preset_region(img, preset, valid_cascades):
    """
    Check if the preset coordinates actually contain a webcam / face in this specific clip.
    If the streamer streamed without camera (or has donation text at the preset), reject it.
    """
    try:
        import cv2
        h, w = img.shape[:2]
        scale_x = w / 1920.0
        scale_y = h / 1080.0

        px = int(preset["x"] * scale_x)
        py = int(preset["y"] * scale_y)
        pw = int(preset["w"] * scale_x)
        ph = int(preset["h"] * scale_y)

        # Expand slightly for tolerance
        x1 = max(0, min(w - 1, px - 20))
        y1 = max(0, min(h - 1, py - 20))
        x2 = max(x1 + 1, min(w, px + pw + 20))
        y2 = max(y1 + 1, min(h, py + ph + 20))

        crop = img[y1:y2, x1:x2]
        if crop.size == 0:
            return False

        # Require an actual face cascade detection inside the preset region
        gray_crop = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
        for cascade_path in valid_cascades:
            face_cascade = cv2.CascadeClassifier(cascade_path)
            faces = face_cascade.detectMultiScale(
                gray_crop, scaleFactor=1.1, minNeighbors=4, minSize=(35, 35)
            )
            for (fx, fy, fw, fh) in faces:
                face_subcrop = crop[fy:fy+fh, fx:fx+fw]
                if has_skin_tones(face_subcrop):
                    return True

        return False
    except Exception:
        return False

def is_in_game_hud(cx, cy, fw, fh, game_kind):
    """
    Check if candidate face coordinates fall into known game HUD areas or combat arenas.
    """
    gk = (game_kind or "lol").lower()

    if gk == "lol":
        # 1. League of Legends Top-Left Target/Champion Inspector Frame & Donation text:
        if cx < 400 and cy < 280:
            return True

        # 2. Top scoreboard / kill scores / timers / donation goals:
        if cy < 160:
            return True

        # 3. Center combat arena & TAB scoreboard (covers x: 450..1470, y: 130..880):
        if 450 <= cx <= 1470 and 130 <= cy <= 880:
            return True

        # 4. Bottom ability bar (skills, items, own portrait):
        if cy > 780 and 380 <= cx <= 1540:
            return True

        # 5. Bottom-Right minimap:
        if cx > 1580 and cy > 760:
            return True

    elif gk == "valorant":
        # 1. Top round timer, agent portraits & donation bars:
        if cy < 160:
            return True

        # 2. Top-Left minimap:
        if cx < 380 and cy < 380:
            return True

        # 3. Center combat arena & crosshair / TAB scoreboard:
        if 480 <= cx <= 1440 and 160 <= cy <= 880:
            return True

        # 4. Bottom abilities / ammo / weapons bar:
        if cy > 840 and 380 <= cx <= 1540:
            return True

    else:
        # Generic game exclusions: center arena and extreme edges
        if 480 <= cx <= 1440 and 140 <= cy <= 880:
            return True
        if cy < 140:
            return True

    return False

def detect_facecam(frame_path, streamer_name="", game_kind="lol"):
    clean_streamer = (streamer_name or "").strip().lower().replace("@", "")

    # Resolve OpenCV cascades if available
    valid_cascades = []
    eye_cascades = []
    cv2_module = None
    try:
        import cv2
        cv2_module = cv2
        valid_cascades = [p for p in CASCADE_CANDIDATE_PATHS if os.path.exists(p)]
        eye_cascades = [p for p in EYE_CASCADE_CANDIDATE_PATHS if os.path.exists(p)]
        if hasattr(cv2, 'data') and hasattr(cv2.data, 'haarcascades'):
            for std_name in ["haarcascade_frontalface_alt2.xml", "haarcascade_frontalface_alt.xml", "haarcascade_frontalface_default.xml", "haarcascade_profileface.xml"]:
                std_path = cv2.data.haarcascades + std_name
                if os.path.exists(std_path) and std_path not in valid_cascades:
                    valid_cascades.append(std_path)
            for std_eye in ["haarcascade_eye.xml", "haarcascade_eye_tree_eyeglasses.xml"]:
                eye_path = cv2.data.haarcascades + std_eye
                if os.path.exists(eye_path) and eye_path not in eye_cascades:
                    eye_cascades.append(eye_path)
    except Exception:
        pass

    # 1. Preset Check with Image Verification
    # If a streamer has a preset, verify that the preset ROI actually contains a face/webcam in this clip.
    # If the streamer streamed without a webcam (e.g. donation goal at 10,10), reject preset so it falls back to Option B!
    for preset_name, coords in STREAMER_PRESETS.items():
        if preset_name in clean_streamer:
            if frame_path and os.path.exists(frame_path) and cv2_module is not None:
                try:
                    img = cv2_module.imread(frame_path)
                    if img is not None:
                        if verify_preset_region(img, coords, valid_cascades):
                            return coords
                        # Preset ROI has no verified face -> continue to full frame search
                        break
                except Exception:
                    return coords
            else:
                return coords

    # 2. Full Frame Computer Vision Face Detection
    if frame_path and os.path.exists(frame_path) and cv2_module is not None and valid_cascades:
        try:
            cv2 = cv2_module
            img = cv2.imread(frame_path)
            if img is not None:
                h, w = img.shape[:2]
                gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

                candidate_faces = []

                for cascade_path in valid_cascades:
                    face_cascade = cv2.CascadeClassifier(cascade_path)
                    # minNeighbors=6 and minSize=(55, 55) strictly prevents false positives on text and icons
                    faces = face_cascade.detectMultiScale(
                        gray, scaleFactor=1.08, minNeighbors=6, minSize=(55, 55)
                    )

                    for (fx, fy, fw, fh) in faces:
                        scale_x = 1920.0 / w
                        scale_y = 1080.0 / h
                        real_fx = int(fx * scale_x)
                        real_fy = int(fy * scale_y)
                        real_fw = int(fw * scale_x)
                        real_fh = int(fh * scale_y)
                        cx = real_fx + real_fw // 2
                        cy = real_fy + real_fh // 2

                        # Face bounding box aspect ratio check: must be reasonably proportional (0.75 <= h/w <= 1.45)
                        # Text lines and banners have extreme aspect ratios and are filtered out immediately
                        aspect_ratio = real_fh / float(max(1, real_fw))
                        if aspect_ratio < 0.75 or aspect_ratio > 1.45:
                            continue

                        # Filter out in-game HUDs, scoreboard, minimap, and central combat arena
                        if is_in_game_hud(cx, cy, real_fw, real_fh, game_kind):
                            continue

                        # Extract original resolution crop for skin tone verification
                        crop_y1 = max(0, min(h - 1, fy))
                        crop_x1 = max(0, min(w - 1, fx))
                        crop_y2 = max(crop_y1 + 1, min(h, fy + fh))
                        crop_x2 = max(crop_x1 + 1, min(w, fx + fw))
                        face_crop = img[crop_y1:crop_y2, crop_x1:crop_x2]

                        # Verify human skin tones in dual YCrCb + HSV space
                        if not has_skin_tones(face_crop):
                            continue

                        # Calculate score based on webcam quadrant preference & face size
                        score = real_fw * real_fh
                        if cx >= 1350 and cy >= 650:  # Bottom right (most common in LoL)
                            score *= 1.4
                        elif cx <= 450 and cy >= 650:  # Bottom left
                            score *= 1.3
                        elif cx >= 1350 and cy <= 450: # Top right
                            score *= 1.3
                        elif cx <= 450 and (350 <= cy <= 750): # Middle left
                            score *= 1.2

                        candidate_faces.append({
                            "fx": real_fx, "fy": real_fy,
                            "fw": real_fw, "fh": real_fh,
                            "cx": cx, "cy": cy,
                            "score": score
                        })

                if candidate_faces:
                    # Pick highest scoring face
                    best = sorted(candidate_faces, key=lambda f: f["score"], reverse=True)[0]
                    bfx, bfy, bfw, bfh = best["fx"], best["fy"], best["fw"], best["fh"]
                    bcx, bcy = best["cx"], best["cy"]

                    # Exact 1080:680 ratio (1.5862) to prevent any aspect-ratio stretching or cropping in FFmpeg
                    cam_w = 460
                    cam_h = 290

                    # Natural head framing with generous headroom (75% of face height above eyebrows)
                    # Prevents head, hair, headphones or caps from being truncated
                    headroom = int(bfh * 0.75)
                    cam_y = max(0, min(1080 - cam_h, bfy - headroom))
                    cam_x = max(0, min(1920 - cam_w, bcx - cam_w // 2))

                    return {
                        "has_facecam": True,
                        "x": cam_x,
                        "y": cam_y,
                        "w": cam_w,
                        "h": cam_h
                    }
        except Exception:
            pass

    # 3. Clean fallback: No facecam detected -> Render Option B (Ambient Blur)
    return {"has_facecam": False}

if __name__ == "__main__":
    frame_arg = sys.argv[1] if len(sys.argv) > 1 else ""
    streamer_arg = sys.argv[2] if len(sys.argv) > 2 else ""
    game_arg = sys.argv[3] if len(sys.argv) > 3 else "lol"
    result = detect_facecam(frame_arg, streamer_arg, game_arg)
    print(json.dumps(result))

