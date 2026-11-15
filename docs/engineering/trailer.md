# The trailer: how it is made and how to make it again

The trailer is produced from the game itself, with no editing program: a recorder inside the player films a shot list, and a Python
edit cuts, captions and scores the clips. Everything is data, so a different cut or a better shot is a re-run, not a rework.

```
make_shots.py ──► job.json ──► shoot.ps1 ──► player (-trailer) ──► take/*.mp4 ──► make_trailer.py ──► prying_eyes_trailer.mp4
 (shot list)                   (low priority)  (recorder)            (2560x1440)     (edit + captions + title + score)
```

## Pieces

| Piece | Where | What it does |
| --- | --- | --- |
| Recorder | `Scripts/Proc/TrailerJob.cs`, `TrailerCapture.cs`, `FloorDirector.Trailer.cs` (all `#if !PE_DISTRIBUTION_BUILD`) | `-trailer job.json`: builds a floor from a seed, flies a free camera or plays first person with a weapon, spawns enemies, fires `fire / burst / kill / ads / slow / torch / reload / equip` events, renders to a RenderTexture and pipes raw RGB to ffmpeg (mp4 per shot) or writes stills |
| Floor maps | `tools/trailer/floorlib.py` | reads the scout dump of a floor: free cells, straight lanes, a map PNG; `make_shots.py` uses it to warn about cameras close to obstacles |
| Shot list | `tools/trailer/make_shots.py` | the shots (church, theatre, sanatorium, first person) as Python; writes the job file |
| Shooting | `tools/trailer/shoot.ps1` | runs `Builds\Audit\PryingEyes.exe` at BelowNormal priority; refuses to start if another player is running (the GPU is shared) |
| Contact sheets | `preview_sheet.py` (stills), `clip_sheet.py` (finished clips), `contact_sheet.py` (scout) | to look at the work without opening a video |
| Edit | `tools/trailer/make_trailer.py` | `SEGMENTS` (clip, in point, length, dissolve) and `CAPTIONS` are the whole edit; builds one ffmpeg filter graph |
| Captions and title | `tools/trailer/trailer_title.py` | caption PNGs (Georgia from `C:\Windows\Fonts`) and the procedural title card: the eye that opens, the name, the line |
| Score | `tools/trailer/trailer_audio.py`, `trailer_score.py` | the game's own samples plus a synthesised drone, pulse, bass, risers and hits, placed by time; loudness and limiter |
| Checks | `tools/trailer/trailer_check.py`, `tools/test_trailer.py` | `info`, `sheet` (3 frames per segment), `audio` (loudness, per-second level, spectrogram); unit tests of the timeline |

Filmed clips and renders live under `unity_logs/trailer/` and are not committed.

## Remaking it

```
# 1. a player that contains the recorder (the code is in PryingEyes_Data\Managed\Assembly-CSharp.dll: a scripts-only build is enough)
# 2. the shots
python tools/trailer/make_shots.py --mode film --out unity_logs/trailer/take1 --width 2560 --height 1440 --crf 14
powershell -NoProfile -ExecutionPolicy Bypass -File tools\trailer\shoot.ps1 -Job unity_logs\trailer\take1.json
# a few shots again, in front of the full take (the first folder that has a clip wins)
python tools/trailer/make_shots.py --mode film --out unity_logs/trailer/take2 --width 2560 --height 1440 --crf 14 --only f01,h01
# 3. the edit: 960x540 in a few seconds, then the real one
python tools/trailer/make_trailer.py --preview --out unity_logs/trailer/work/preview.mp4
python tools/trailer/make_trailer.py --out unity_logs/trailer/prying_eyes_trailer.mp4 --crf 20
# 4. look at it
python tools/trailer/trailer_check.py sheet unity_logs/trailer/prying_eyes_trailer.mp4 unity_logs/trailer/work/check
python tools/trailer/trailer_check.py audio unity_logs/trailer/prying_eyes_trailer.mp4 unity_logs/trailer/work/check
```

Use `--mode preview` in `make_shots.py` for three stills per shot (cheap, to place a camera before filming it).

## The shape of the film

Act I (about 24 s) is slow and joined by cross-dissolves: the lift, the church, a theatre, a sanatorium, the eye window. From the
first monster on, every cut is a whole number of beats at 100 BPM (0.6 s), so the kick of the score lands on every cut: the
residents (act II), the arsenal (act III, seven weapons in first person), the horde (act IV), then the title card.

## Things that would otherwise cost an afternoon

- **No `-nographics`.** The recorder needs the real GPU. In batchmode the cameras do not render by themselves, so the recorder
  renders its own camera into a RenderTexture in `LateUpdate` (execution order 32000) and reads it back.
- **No audio is captured.** Batchmode has no audio output, so every sound of the film is placed from the shot events (`fire`,
  `burst`, `kill`, `slow`) and the edit's timeline. Burst shots are frame-quantised the way the recorder fires them
  (`trailer_score.shot_times`); checked on the finished film, every gunshot falls on the frame of its muzzle flash (offset 0).
- **Film time is not world time.** A `slow` event sets `Time.timeScale` while the camera path keeps running on film time; audio and
  captions use film time.
- **Enemies that start far away and slowly look like statues.** Spawn them 6-12 m from the camera with `speed` 1.4 or more
  (the monk's walk cycle follows its speed up to 2.2x); for kills use a `kill` event (a headshot of 99999 damage), enemies have
  100000 hit points so that nothing dies early.
- **White door frames** (`Cornici di tipo`) are debug outlines; `TrailerBuild` hides them.
- **ffmpeg 8** reads a filter graph with `-/filter_complex file`; `-filter_complex_script` is gone. `cmd /c` mangles a quoted
  executable path: pass paths without quotes, or start the player from PowerShell as `shoot.ps1` does.
- **Loudness** is measured, not guessed: the score is normalised to -16 LUFS integrated with a look-ahead limiter at -1.5 dBFS.
  The first act is mixed about 10 dB under the action on purpose; `trailer_check.py audio` prints the level of every second.
- The fonts are Windows system fonts (Georgia). On another machine change `FONTS` in `trailer_title.py`.
