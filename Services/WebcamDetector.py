#!/usr/bin/env python3
"""
WebcamDetector.py
Lightweight detector for streamer facecams in Valorant gameplay clips.
Uses known streamer presets and OpenCV multi-cascade face detection.
Outputs JSON: {"has_facecam": bool, "x": int, "y": int, "w": int, "h": int}
"""

import sys
import json
import os

# Known static streamer webcam positions on 1920x1080 canvas
STREAMER_PRESETS = {
    "sinatraa": {"has_facecam": True, "x": 10, "y": 60, "w": 480, "h": 360},
    "tarik": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 340},
    "tenz": {"has_facecam": True, "x": 1440, "y": 120, "w": 460, "h": 340},
    "s0m": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 340},
    "kyedae": {"has_facecam": True, "x": 40, "y": 160, "w": 450, "h": 340},
    "subroza": {"has_facecam": True, "x": 30, "y": 680, "w": 440, "h": 340},
    "shanks": {"has_facecam": True, "x": 30, "y": 680, "w": 440, "h": 340},
    "fns": {"has_facecam": True, "x": 30, "y": 680, "w": 440, "h": 340},
    "hiko": {"has_facecam": True, "x": 30, "y": 140, "w": 440, "h": 340},
    "minibo0": {"has_facecam": True, "x": 15, "y": 30, "w": 420, "h": 360},
    "zikz": {"has_facecam": True, "x": 1540, "y": 480, "w": 380, "h": 400},
    "shahzam": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 340},
    "aspas": {"has_facecam": True, "x": 20, "y": 680, "w": 440, "h": 340},
    "boaster": {"has_facecam": True, "x": 30, "y": 120, "w": 440, "h": 340},
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
    clean_streamer = (streamer_name or "").strip().lower()
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

                # Find valid cascade files
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

                    for (fx, fy, fw, fh) in faces:
                        scale_x = 1920.0 / w
                        scale_y = 1080.0 / h
                        cx = int((fx + fw / 2.0) * scale_x)
                        cy = int((fy + fh / 2.0) * scale_y)

                        # Exclude Top HUD (agent portraits y < 110)
                        if cy < 110:
                            continue
                        # Exclude Center crosshair/agent combat zone
                        if 560 <= cx <= 1360:
                            continue
                        # Exclude Bottom HUD ability icons
                        if cy > 920 and (500 <= cx <= 1420):
                            continue

                        # Found streamer webcam face! Expand to camera viewport box
                        cam_w = 460
                        cam_h = 340
                        cam_x = max(0, min(1920 - cam_w, cx - cam_w // 2))
                        cam_y = max(0, min(1080 - cam_h, cy - cam_h // 2))

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
