# Arm clips from generated video

Clip-led arms (`content/held/clips.json`) play baked clips on the PSX arms. This folder makes one from a
third-person MiniMax H3 video: the video model supplies the motion, a pose model reads the body, and the arms are
aimed where the video's arms point. The video's look does not matter, only its motion.

1. **Generate** a full-body, fixed-camera video of the action (`h3.py`, on a ComfyUI host with the H3 models, such as
   the owner's 5090). Ask for the action to be staged for a first-person view: the man facing the camera, the cut
   crossing in front of his body. Cuts that finish beside or behind the body leave the view. Try a few seeds and
   pick by eye.
2. **Read the pose** (`extract.py`, MediaPipe Pose Landmarker heavy, CPU): 33 world landmarks per frame. Check the
   arm on an overlay of the frames before going on.
3. **Retarget** (`retarget.py`): a clip pack on the PSX skeleton with one clip. Each upper arm and forearm points
   where the video's does, bent in the video's elbow plane; each hand turns to the video's wrist-knuckles line and
   index-pinky side; fingers and clavicles hold `knife_idle`'s first frame (the fist closed on a grip). The view is
   the body's facing over `--facing` frames. `--pitch` and `--forward` stage the motion for the view. `--report`
   gives the right wrist's speed and height per frame, to place the action's `ClipMarks`.
4. **Use**: list the pack in `content/held/rigs/psx.json` `packs`, name the clip and its marks in `clips.json`, and
   check it in game (`riders.dev.select`, `riders.dev.use main` and frame captures in held time).

Each clip keeps its source video, landmarks and settings beside it (for example `art/arms/h3-slash/provenance.json`).

Setup: `python3.12 -m venv venv && venv/bin/pip install mediapipe opencv-python-headless numpy`, and the model
`https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/latest/pose_landmarker_heavy.task`.

Limits: one camera view, so depth is MediaPipe's estimate; the forearm's roll rests on two hand points. No twist
joints on the PSX arms. The clip carries the body's own timing; `ClipMarks` fit it to the action's phases.
