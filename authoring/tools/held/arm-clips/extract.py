"""MediaPipe pose world landmarks per video frame -> JSON (33 x [x,y,z,visibility]) plus image landmarks."""
import json, sys, cv2, mediapipe as mp
from mediapipe.tasks.python import vision, BaseOptions
video, model, out = sys.argv[1:4]
opts = vision.PoseLandmarkerOptions(base_options=BaseOptions(model_asset_path=model), running_mode=vision.RunningMode.VIDEO,
    min_pose_detection_confidence=0.3, min_tracking_confidence=0.3)
lm = vision.PoseLandmarker.create_from_options(opts)
cap = cv2.VideoCapture(video); fps = cap.get(cv2.CAP_PROP_FPS) or 24; frames = []; i = 0
while True:
    ok, img = cap.read()
    if not ok: break
    r = lm.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(img, cv2.COLOR_BGR2RGB)), int(i * 1000 / fps))
    if r.pose_world_landmarks:
        w = [[p.x, p.y, p.z, p.visibility] for p in r.pose_world_landmarks[0]]
        n = [[p.x, p.y, p.z, p.visibility] for p in r.pose_landmarks[0]]
    else: w = n = None
    frames.append({"world": w, "image": n}); i += 1
json.dump({"fps": fps, "size": [int(cap.get(3)), int(cap.get(4))], "frames": frames}, open(out, "w"))
print(video, len(frames), "frames,", sum(f["world"] is not None for f in frames), "with a pose")
