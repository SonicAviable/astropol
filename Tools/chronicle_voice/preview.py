#!/usr/bin/env python3
"""
Превью летописи фракции без Unity: ролик 1280×720 с иллюстрациями, титрами, субтитрами, голосом,
музыкой (приглушается под голосом), ударами и нарастанием — тайминги как в FactionHistoryWindow.
Нужен ffmpeg. Запуск из корня репозитория:
    python3 Tools/chronicle_voice/preview.py xarn /tmp/xarn.mp4
"""
import sys, os, subprocess, textwrap, re
sys.path.insert(0, 'Tools/chronicle_voice'); import generate as g
key = sys.argv[1]; out = sys.argv[2]; W, H, FPS = 1280, 720, 30
S = os.path.dirname(os.path.abspath(out)); tmp = os.path.join(S, "pv_" + key); os.makedirs(tmp, exist_ok=True)
src = open("Assets/Scripts/Core/FactionHistory.cs", encoding="utf-8").read()
blk = src[src.index(f'F("{key}"'):]; blk = blk[:blk.index("Finale(")+2000]
music = re.search(r'F\("%s", "([^"]+)", ([\d.]+)f' % key, src)
chs = re.findall(r'C\(Loc\.T\("([^"]+)"\), Loc\.T\("([^"]+)"\), "([^"]+)"', blk)
fin_art = re.search(r'Finale\("([^"]+)"', blk).group(1)
lines = dict(g.parse_lines()[key])
def dur(f): return float(subprocess.run(["ffprobe","-v","error","-show_entries","format=duration","-of","csv=p=0",f],capture_output=True,text=True).stdout)
def img(a):
    for ext in (".jpg",".png"):
        p = "Assets/Resources/" + a + ext
        if os.path.exists(p): return p
segs = []; t = 0.0; voices = []
for i in range(5):
    c = str(i) if i < 4 else "f"
    title, date, art = (chs[i] if i < 4 else ("", "2200 год", fin_art))
    st = t; lt = st + 1.7; subs = []
    for j, ln in enumerate(lines[c]):
        f = f"Assets/Resources/Audio/Chronicle/{key}_{c}_{j}.ogg"; d = dur(f)
        voices.append((f, lt)); subs.append((lt, lt + d, ln)); lt += d + 0.55
    end = lt - 0.55 + (1.4 if i < 3 else 3.1 if i == 3 else 3.0)
    segs.append(dict(i=i, start=st, end=end, title=title, date=date, art=img(art), subs=subs)); t = end
total = t
def tf(name, text):
    p = os.path.join(tmp, name); open(p, "w", encoding="utf-8").write(text); return p
FT = "Assets/Resources/Fonts/RussoOne-Regular.ttf"; FR = "Assets/Resources/Fonts/Play-Regular.ttf"; FB = "Assets/Resources/Fonts/Play-Bold.ttf"
parts = []
for s in segs:
    L = s["end"] - s["start"] + (1.6 if s["i"] < 4 else 0); N = int(L * FPS)
    zin = s["i"] % 2 == 0 or s["i"] == 4
    z0, z1 = (1.0, 1.12) if zin else (1.12, 1.0)
    vf = [f"scale=2560:-2,setsar=1",
          f"zoompan=z='{z0}+({z1}-{z0})*on/{N}':x='(iw-iw/zoom)*({0.3 if s['i']%2==0 else 0.7}+{0.4 if s['i']%2==0 else -0.4}*on/{N})':y='(ih-ih/zoom)/2':d={N}:s={W}x{H}:fps={FPS}",
          "eq=saturation=1.05:contrast=1.05", "vignette=PI/4.2",
          f"drawbox=x=0:y=ih*0.55:w=iw:h=ih*0.45:color=black@0.35:t=fill"]
    if s["i"] < 4:
        no = tf(f"no{s['i']}.txt", "ГЛАВА " + ["I","II","III","IV"][s["i"]])
        ti = tf(f"ti{s['i']}.txt", s["title"].upper()); da = tf(f"da{s['i']}.txt", s["date"])
        fo = f"min(1,max(0,(t-0.3)/0.8))*min(1,max(0,({s['end']-s['start']}-t)/0.8))"
        vf += [f"drawtext=fontfile={FB}:textfile={no}:fontsize=15:fontcolor=0xFF8C80:alpha='{fo}':x=100:y=h*0.5-70",
               f"drawtext=fontfile={FT}:textfile={ti}:fontsize=54:fontcolor=white:alpha='{fo}':x=100:y=h*0.5-45:shadowcolor=black@0.8:shadowy=3",
               f"drawbox=x=100:y=ih*0.5+25:w='min(420,420*(t-0.6)/1.1)':h=2:color=0xFF8C80@0.9:t=fill:enable='gte(t,0.6)'",
               f"drawtext=fontfile={FR}:textfile={da}:fontsize=17:fontcolor=0xDDD6CC:alpha='{fo}':x=100:y=h*0.5+40"]
    else:
        nm = tf("fin_name.txt", {"xarn":"ДОМИНИОН КСАРН","astrea":"РЕСПУБЛИКА АСТРЕЯ","aquila":"СИНДИКАТ АКВИЛА","iridia":"ИРИДИЙСКИЙ ОРАКУЛ","terraan":"КОНКЛАВ ТЭРРА'АН","nol":"КОНСЕНСУС «НОЛЬ»"}[key])
        tag = tf("fin_tag.txt", "ВАШЕ ПРАВЛЕНИЕ НАЧИНАЕТСЯ")
        fo = "min(1,max(0,(t-1.1)/1.2))"
        vf += [f"drawtext=fontfile={FB}:textfile={tag}:fontsize=15:fontcolor=0xFF8C80:alpha='{fo}':x=100:y=h*0.5-80",
               f"drawtext=fontfile={FT}:textfile={nm}:fontsize=50:fontcolor=white:alpha='{fo}':x=100:y=h*0.5-55:shadowcolor=black@0.8:shadowy=3"]
    for k, (a, b, ln) in enumerate(s["subs"]):
        a -= s["start"]; b -= s["start"]
        wrapped = "\n".join(textwrap.wrap(ln, 62))
        sp = tf(f"s{s['i']}_{k}.txt", wrapped)
        al = f"min(1,max(0,(t-{a:.2f})/0.35))*min(1,max(0,({b+0.45:.2f}-t)/0.3))"
        vf.append(f"drawtext=fontfile={FR}:textfile={sp}:fontsize=24:line_spacing=6:fontcolor=0xF7F2E6:alpha='{al}':x=(w-text_w)/2:y=h-61-text_h-22:shadowcolor=black@0.9:shadowy=2:enable='between(t,{a:.2f},{b+0.8:.2f})'")
    vf += [f"drawbox=x=0:y=0:w=iw:h=61:color=black:t=fill", f"drawbox=x=0:y=ih-61:w=iw:h=61:color=black:t=fill",
           f"noise=alls=7:allf=t"]
    po = os.path.join(tmp, f"seg{s['i']}.mp4")
    subprocess.run(["ffmpeg","-y","-loglevel","error","-loop","1","-i",s["art"],"-t",f"{L:.2f}","-vf",",".join(vf),"-r",str(FPS),"-pix_fmt","yuv420p","-c:v","libx264","-preset","veryfast","-crf","20",po],check=True)
    parts.append((po, s["start"]))
# склейка с наплывами
inputs = sum([["-i", p] for p, _ in parts], []); fc = []; prev = "[0:v]"
for k in range(1, len(parts)):
    fc.append(f"{prev}[{k}:v]xfade=transition=fade:duration=1.6:offset={parts[k][1]:.2f}[v{k}]"); prev = f"[v{k}]"
video = os.path.join(tmp, "video.mp4")
subprocess.run(["ffmpeg","-y","-loglevel","error",*inputs,"-filter_complex",";".join(fc),"-map",prev,"-c:v","libx264","-preset","veryfast","-crf","20","-pix_fmt","yuv420p",video],check=True)
# звук: музыка + голос + удары + нарастание
ain = ["-i", "Assets/Resources/" + music.group(1) + (".ogg" if os.path.exists("Assets/Resources/"+music.group(1)+".ogg") else ".mp3" if os.path.exists("Assets/Resources/"+music.group(1)+".mp3") else ".wav")]
mix = [f"[0:a]atrim=0:{total+1:.2f},volume={float(music.group(2))*0.6:.2f},afade=t=in:d=3,afade=t=out:st={total-2:.2f}:d=2[m]"]
labels = ["[m]"]; n = 1
for f, at in voices:
    ain += ["-i", f]; mix.append(f"[{n}:a]adelay={int(at*1000)}|{int(at*1000)},aformat=channel_layouts=stereo[a{n}]"); labels.append(f"[a{n}]"); n += 1
for s in segs:
    ain += ["-i", "Assets/Resources/Audio/Chronicle/sfx_impact.ogg"]; ms = int(s["start"]*1000)
    mix.append(f"[{n}:a]volume=0.8,adelay={ms}|{ms}[a{n}]"); labels.append(f"[a{n}]"); n += 1
ain += ["-i", "Assets/Resources/Audio/Chronicle/sfx_riser.ogg"]; ms = int((segs[4]["start"]-3.1)*1000)
mix.append(f"[{n}:a]volume=0.5,adelay={ms}|{ms}[a{n}]"); labels.append(f"[a{n}]"); n += 1
# приглушение музыки под голосом — sidechain по голосовой дорожке
vo = "".join(labels[1:1+len(voices)])
mix[0] = mix[0].replace("[m]", "[m0]")
mix.append(f"{vo}amix=inputs={len(voices)}:normalize=0,asplit[vo1][vo2]")
mix.append(f"[m0][vo1]sidechaincompress=threshold=0.03:ratio=4:attack=60:release=700[m]")
rest = "".join(labels[1+len(voices):])
mix.append(f"[m][vo2]{rest}amix=inputs={2+len(labels)-1-len(voices)}:normalize=0,alimiter=limit=0.95[aout]")
audio = os.path.join(tmp, "audio.m4a")
subprocess.run(["ffmpeg","-y","-loglevel","error",*ain,"-filter_complex",";".join(mix),"-map","[aout]","-t",f"{total+0.5:.2f}","-c:a","aac","-b:a","192k",audio],check=True)
subprocess.run(["ffmpeg","-y","-loglevel","error","-i",video,"-i",audio,"-c","copy","-shortest",out],check=True)
print(out, f"{total:.1f}s")
