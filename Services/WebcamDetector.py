#!/usr/bin/env python3
"""
WebcamDetector.py
Lightweight detector for streamer facecams in LoL & Valorant gameplay clips.
Uses known streamer presets and OpenCV multi-cascade face detection.
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
    "/usr/share/opencv4/haarcascades/haarcascade_frontalface_alt.xml",
    "/usr/share/opencv4/haarcascades/haarcascade_frontalface_default.xml",
    "/usr/share/opencv4/haarcascades/haarcascade_frontalface_alt2.xml",
    "/usr/share/opencv/haarcascades/haarcascade_frontalface_alt.xml",
    "/usr/share/opencv/haarcascades/haarcascade_frontalface_default.xml",
]

def detect_facecam(frame_path, streamer_name=""):
    # 1. Check known streamer presets first
    clean_streamer = (streamer_name or "").strip().lower().replace("@", "")
    for preset_name, coords in STREAMER_PRESETS.items():
        if preset_name in clean_streamer:
            return coords

    # 2. Try OpenCV Haar Cascade if frame exists
    if frame_path and os.path.exists(frame_path):
        try:
            import cv2
            img = cv2.imread(frame_path)
            if img is not None:
                h, w = img.shape[:2]
                gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

                valid_cascades = [p for p in CASCADE_CANDIDATE_PATHS if os.path.exists(p)]
                if hasattr(cv2, 'data') and hasattr(cv2.data, 'haarcascades'):
                    std_path = cv2.data.haarcascades + "haarcascade_frontalface_alt.xml"
                    if os.path.exists(std_path) and std_path not in valid_cascades:
                        valid_cascades.insert(0, std_path)

                for cascade_path in valid_cascades:
                    face_cascade = cv2.CascadeClassifier(cascade_path)
                    faces = face_cascade.detectMultiScale(
                        gray, scaleFactor=1.08, minNeighbors=3, minSize=(45, 45)
                    )

                    candidate_faces = []
                    for (fx, fy, fw, fh) in faces:
                        scale_x = 1920.0 / w
                        scale_y = 1080.0 / h
                        real_fx = int(fx * scale_x)
                        real_fy = int(fy * scale_y)
                        real_fw = int(fw * scale_x)
                        real_fh = int(fh * scale_y)
                        cx = real_fx + real_fw // 2
                        cy = real_fy + real_fh // 2

                        # Exclude Top HUD (agent/champion portraits y < 110)
                        if cy < 110:
                            continue
                        # Exclude Center crosshair / champion combat arena
                        if 600 <= cx <= 1320:
                            continue
                        # Exclude Bottom HUD ability bars
                        if cy > 920 and (500 <= cx <= 1420):
                            continue

                        candidate_faces.append((real_fx, real_fy, real_fw, real_fh, cx, cy))

                    if candidate_faces:
                        # Pick dominant face
                        best_face = sorted(candidate_faces, key=lambda f: f[2] * f[3], reverse=True)[0]
                        bfx, bfy, bfw, bfh, bcx, bcy = best_face

                        # Compute clean 16:9 camera box without game overflow
                        cam_w = 430
                        cam_h = 265  # 16:9 aspect ratio

                        # Head sits in upper 35% of camera frame
                        cam_y = max(0, min(1080 - cam_h, bfy - int(bfh * 0.35)))
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

    # 3. Fallback: No facecam detected
    return {"has_facecam": False}

if __name__ == "__main__":
    frame_arg = sys.argv[1] if len(sys.argv) > 1 else ""
    streamer_arg = sys.argv[2] if len(sys.argv) > 2 else ""
    result = detect_facecam(frame_arg, streamer_arg)
    print(json.dumps(result))
