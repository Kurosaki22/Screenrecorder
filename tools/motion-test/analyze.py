"""Time-aware evenness: compares each bar step with speed * real frame interval (works for VFR too).
Usage: analyze_t.py <video> <width> <row>. Error is reported in display refreshes (240 Hz = 4.17 ms)."""
import statistics
import subprocess
import sys

video, width, row = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
pts = [float(x) for x in subprocess.run(
    ["ffprobe", "-v", "error", "-select_streams", "v", "-show_entries", "frame=pts_time", "-of", "csv=p=0", video],
    capture_output=True, text=True, check=True).stdout.split()]
raw = subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", video, "-fps_mode", "passthrough",
                      "-vf", f"format=gray,crop={width}:1:0:{row}", "-f", "rawvideo", "-"],
                     capture_output=True, check=True).stdout
pos = []
for i in range(0, len(raw) - width + 1, width):
    bright = [x for x, v in enumerate(raw[i:i + width]) if v > 128]
    pos.append(sum(bright) / len(bright) if bright else None)
n = min(len(pos), len(pts))
pairs = [(pos[i] - pos[i - 1], pts[i] - pts[i - 1]) for i in range(1, n)
         if pos[i] is not None and pos[i - 1] is not None and pts[i] > pts[i - 1] and pos[i] - pos[i - 1] > -width / 3]
speed = statistics.median(d / dt for d, dt in pairs)        # px per second
refresh_px = speed / 240                                     # bar movement per display refresh
err = [abs(d - speed * dt) / refresh_px for d, dt in pairs]  # in refreshes
ok = sum(e < 0.5 for e in err)
stalls = sum(abs(d) < 1 for d, _ in pairs)
gaps = [dt for _, dt in pairs]
print(f"{video.replace(chr(92), '/').split('/')[-1]:<14} frames={n:4} speed={speed:6.0f}px/s "
      f"on_time={100 * ok / len(err):5.1f}% off>=1refresh={sum(e >= 1 for e in err):3} stalls={stalls:3} "
      f"interval={1000 * statistics.median(gaps):5.2f}ms max={1000 * max(gaps):5.1f}ms")
