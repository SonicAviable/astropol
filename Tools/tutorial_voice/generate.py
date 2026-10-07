#!/usr/bin/env python3
"""
Озвучка обучения: голос ИИ-советника ОРАКУЛ-7.

Синтез — RHVoice (голос aleksandr-hq), затем «роботизация» через ffmpeg:
лёгкое понижение тона, короткое металлическое эхо, кольцевая модуляция,
немного «цифрового» огрубления и сигнал канала связи в начале фразы.
Результат — Assets/Resources/Audio/Tutorial/*.ogg (TutorialManager грузит их по имени).

Нужно: RHVoice-test (apt: rhvoice rhvoice-russian) и ffmpeg.
Запуск из корня репозитория:  python3 Tools/tutorial_voice/generate.py
Фразы — ниже; номер шага tut_NN совпадает с порядком шагов в TutorialManager.BuildSteps.
"""
import os
import subprocess
import sys
import tempfile
import uuid

VOICE = "aleksandr-hq"
OUT = "Assets/Resources/Audio/Tutorial"

LINES = {
    "tut_01": "Командующий, связь установлена. Я Оракул семь, ваш бортовой советник. "
              "Я проведу вас через первые шаги: время, разведку, границы, колонии и науку. "
              "Подсвеченный элемент — то, с чем нужно поработать.",
    "tut_02": "В левом верхнем углу — правитель вашей империи. Щёлкните по портрету, чтобы открыть обзор империи. "
              "Затем закройте его клавишей Эскейп.",
    "tut_03": "Это ресурсы. Гелий-три питает флот и станции. Титан идёт на стройку, сплавы — на корабли. "
              "Влияние нужно для форпостов и лидеров, а наука двигает исследования. "
              "Наведите курсор на ресурс, чтобы увидеть доходы и расходы.",
    "tut_04": "Галактика живёт в реальном времени. Пробел ставит паузу, клавиши один, два и три меняют скорость. "
              "Запустите время.",
    "tut_05": "Ваш первый инструмент — научный корабль. Выделите его щелчком левой кнопки мыши.",
    "tut_06": "Серые системы ещё не изучены. Щёлкните правой кнопкой мыши по подсвеченной системе, "
              "и корабль отправится на разведку.",
    "tut_07": "Внизу — панель выделенного флота: его состояние, прочность и маршрут. Справа — кнопки управления.",
    "tut_08": "Границы растут форпостами. Их строит строительный корабль. Выделите его.",
    "tut_09": "Отправьте строительный корабль правой кнопкой мыши в изученную соседнюю систему. "
              "Если такой пока нет, ускорьте время и дождитесь разведчика.",
    "tut_10": "Карта умеет показывать политику, ресурсы, коридоры и разведку. Переключите режим карты.",
    "tut_11": "Столица — сердце империи. Дважды щёлкните по её звезде, чтобы войти в систему.",
    "tut_12": "Щёлкните по обитаемой планете — откроется её обзор.",
    "tut_13": "Каждый район даёт рабочие места. Городской — жильё, горнодобывающий — титан, "
              "энергетический — гелий-три, промышленный — сплавы. Население растёт, пока есть жильё.",
    "tut_14": "Наука открывает новые районы, корабли и бонусы. Откройте исследования и выберите направление.",
    "tut_15": "В галактике вы не одни. Дипломатия — это договоры, торговля и войны. "
              "Слабую империю соседи попробуют съесть.",
    "tut_16": "Победить можно доминированием, наукой или по очкам к концу эпохи. "
              "Удачи, Командующий. Оракул семь на связи.",
    # Короткие отклики на выполненное задание (выбираются случайно)
    "tut_ok_1": "Выполнено.",
    "tut_ok_2": "Отлично.",
    "tut_ok_3": "Задача принята.",
}

# Роботизация голоса (вход — моно 24 кГц от RHVoice)
ROBOT = ",".join([
    "highpass=f=120",
    "asetrate=24000*0.94,aresample=24000,atempo=1/0.94",          # чуть ниже тон, тот же темп
    "aecho=0.8:0.7:7|13:0.32|0.22",                                # короткое металлическое эхо
    "aeval='val(0)*(0.78+0.22*sin(2*PI*68*t))':c=same",            # кольцевая модуляция
    "acrusher=bits=12:mode=log:aa=1:mix=0.22",                     # лёгкое цифровое огрубление
    "aecho=0.8:0.4:55:0.12",                                       # «отсек» связи
    "lowpass=f=7200",
    "aresample=44100",
])

# Сигнал канала связи перед фразой: два коротких тона и пауза
BEEP = ("sine=f=1650:d=0.05:sample_rate=44100[b1];sine=f=2200:d=0.05:sample_rate=44100[b2];"
        "anullsrc=r=44100:cl=mono:d=0.03[g1];anullsrc=r=44100:cl=mono:d=0.12[g2];"
        "[b1]volume=0.18[b1v];[b2]volume=0.18[b2v];"
        "[b1v][g1][b2v][g2]concat=n=4:v=0:a=1[beep]")

META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.7
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def run(cmd, **kw):
    subprocess.run(cmd, check=True, **kw)


def main():
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, text in LINES.items():
            raw = os.path.join(tmp, name + ".wav")
            run(["RHVoice-test", "-p", VOICE, "-o", raw], input=text.encode("utf-8"))
            dst = os.path.join(OUT, name + ".ogg")
            beep = not name.startswith("tut_ok")
            graph = (f"{BEEP};[0:a]{ROBOT}[v];[beep][v]concat=n=2:v=0:a=1,loudnorm=I=-16:TP=-1.5[out]"
                     if beep else f"[0:a]{ROBOT},loudnorm=I=-16:TP=-1.5[out]")
            run(["ffmpeg", "-y", "-loglevel", "error", "-i", raw, "-filter_complex", graph,
                 "-map", "[out]", "-ac", "1", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "5", dst])
            meta = dst + ".meta"
            if not os.path.exists(meta):
                with open(meta, "w") as f:
                    f.write(META.format(guid=uuid.uuid4().hex))
            print("ok", dst)


if __name__ == "__main__":
    sys.exit(main())
