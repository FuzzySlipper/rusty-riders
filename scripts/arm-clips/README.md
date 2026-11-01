# Arm clips from any humanoid motion

Clip-led arms (`content/held/clips.json`) play baked clips on the PSX arms. This folder turns a full-body motion
into one: authored clips (Mixamo, the CC0 UAL library, BVH mocap) or a pose read from a video. Every source becomes
the same joint track, so staging and checks never depend on where a motion came from.

1. **Get a motion.**
   - Mixamo: pick X Bot as the character (its standard skeleton), then download the animation as FBX Binary at
     30 fps. Raw Mixamo clips stay out of git, in `content/art/arms/sources/` (synced to the NAS by
     `scripts/art-sync.sh`).
   - UAL and other libraries: a GLB or FBX holding many clips works as is, with `--clip`.
   - Video: `h3.py` generates one on a ComfyUI host with the H3 models. `extract.py` reads it with MediaPipe Pose and
     writes landmarks that `retarget.py` reads directly.
2. **Sample it** (`sample.py`, in Blender), from any FBX, glTF/GLB or BVH. It writes a track: each arm's shoulder,
   elbow, wrist and index/pinky knuckles, the hips, and a point in front of the chest. The facing comes from the
   skeleton's rest pose (toes ahead of ankles), not from how the clip stands. Bone names come from
   `skeletons.json` (Mixamo and UAL so far). A new skeleton family is one entry there.
3. **Retarget and stage** (`retarget.py`). Each upper arm and forearm points where the track's does, bent in its
   elbow plane, and each hand turns to the track's knuckles. Fingers and clavicles hold `knife_idle`'s first frame.
   Staging flags:
   - `--cone` and `--aim`: third-person clips swing behind and over the head, out of a first-person view. Each frame,
     the whole arm turns rigidly about its shoulder until the shoulder-to-wrist line lies in a soft cone around the
     aim. Its bend, elbow plane and wrist turn ride along.
   - `--reach`: opens a folded elbow so the hand is held out where it can be seen.
   - `--clear`: keeps the elbow and forearm at least that far from the view, about 0.3 m. A frame whose arm comes
     closer turns about the shoulder, mostly forward along the view, until it clears, so an overhead wind-up does
     not sweep the elbow across the lens. The camera is the PSX pack's own, or set it with `--camera`. The run
     prints the closest the arm comes. Much more than 0.3 m pushes the hand out of the frame.
   - `--band YAW,PITCH,HALF_YAW,HALF_PITCH`, `--gain`: staging measured from the camera, the one the sword packs use.
     It takes where our camera would see the wrist if the arm did the source's motion. It pulls that bearing into a
     soft ellipse around the centre, spreading it by the gain, then turns the arm about the shoulder to put the wrist
     there. A wide, low band lets a cut sweep across the view and pass below it, as the reference does. Use it with
     `--push=0,-2,1`, which makes `--clear` push down as well as forward, so the forearm passes under the face and
     keeps the cut's sweep. The sword packs use `--band=-15,0,55,28 --gain 2.2 --reach 0.9 --clear 0.28`.
   - `--cone` and `--aim` are the shoulder-centred version. They keep a cut in one part of the view.
   - `--sides R`: drives only the right arm, for example to leave a shield arm out.
   - `--pitch` and `--forward`: the older global tilt that the H3 slash uses.
   - `--report`: gives the right wrist's speed and height per frame, for placing `ClipMarks`.
4. **Preview** (`preview.py`, in Blender). A contact sheet of the clip on the PSX arms from the game's view: the
   pack's camera, which the shoulder plus psx's carry reproduces, at 75 degrees vertical. A bar stands in for a
   blade. `--from side|front|above` shows where the arms go when they leave the view. The framing is close to the
   game's, not exact, so check in game before calling it done.
5. **Measure** (`measure.py`, in Blender) where a clip puts the right hand and elbow in the view, for any first-person
   arms with a known camera: the hand's bearing and distance per frame, the closest the elbow comes, and the share of
   frames with the hand on screen. Compare a pack against the reference below.
6. **Use and check in game.** List the pack in `content/held/rigs/psx.json` `packs`, then set the action's clip and
   marks in `clips.json`. `in-game.sh OUT_DIR` builds the game, runs its own headless host, uses the main hand once
   in held time and writes a contact sheet.

Each pack records its source, licence, commands and marks: `content/art/arms/sources.json` for authored clips,
`art/arms/h3-slash/provenance.json` for the video slash.

Setup: a venv with `mediapipe opencv-python-headless numpy` (the video route), Blender 5.x on PATH, and for video the
MediaPipe model
`https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/latest/pose_landmarker_heavy.task`.

Reference: Dark Messiah's sword arms, measured with `measure.py` on the view model that rusty-bridge imports for
analysis (`first_basic`, in its ignored `local/import`; nothing of it is copied here). The camera is 9 units above
the eye and 2.1 ahead, the vertical field of view 69 degrees, and the screen 16:9. Bearings are degrees right and up
of the view's forward.

| Clip | Length | Hand bearing (right; up) | Hand distance | Elbow closest | Hand on screen |
| --- | --- | --- | --- | --- | --- |
| Rest (`idle`) | loop | 43; -49 (below the frame, the blade rising into it) | 0.71 m | 0.60 m | 0% |
| Fast A | 0.29 s | 45 to -24; -49 to -11 | 0.50-0.71 m | 0.33 m | 75% |
| Fast B (back across) | 0.42 s | -52 to 45; -30 to -9 | 0.46-0.69 m | 0.32 m | 73% |
| Fast C | 0.42 s | -52 to 49; -33 to 4 | 0.33-0.71 m | 0.27 m | 64% |
| Recovery (`end`) | 0.50 s | -44 to 44; -49 to -17 | 0.47-0.71 m | 0.33 m | 46% |
| Power wind-up (`in`) | 0.50 s | 13 to 44; -49 to 14 | 0.42-0.71 m | 0.39 m | 54% |
| Power release (`out`) | 1.08 s | -40 to 58; -49 to 18 | 0.44-0.72 m | 0.32 m | 15% |

What it says: hands rest and finish below the frame and sweep through it. A fast cut crosses about 100 degrees
side to side, mostly below the view's middle, in a third of a second. Elbows stay about 0.27-0.39 m from the
camera. Ours with the shoulder-centred cone: UAL hand 3 to 51 right, on screen 89%; Mixamo 56 to -2 right, 49%.
Both stayed in the right half of the view. With `--band` (2026-10-31): UAL 41 right to 41 left, -51 to 5 up,
elbow 0.31 m, on screen 68%; Mixamo 44 right to 46 left, -55 to 10 up, elbow 0.31 m, 71%.

Limits:
- The PSX arms have no twist joints.
- The cone and clearance work frame by frame and do not look ahead. A low dip can still leave the frame for a moment.
- Video tracks rest on one camera view, so depth is MediaPipe's estimate.
- A clip keeps its source timing; `ClipMarks` fit it to the action's phases.
