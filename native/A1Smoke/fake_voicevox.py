import argparse
import json
import os
import time
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO
from urllib.parse import urlparse, parse_qs

parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, required=True)
parser.add_argument("--output", required=True)
args = parser.parse_args()
os.makedirs(args.output, exist_ok=True)
log_path = os.path.join(args.output, "fake-server-requests.jsonl")

def log(obj):
    with open(log_path, "a", encoding="utf-8") as f:
        f.write(json.dumps(obj, ensure_ascii=False) + "\n")

def mora(text):
    return {
        "text": text,
        "consonant": None,
        "consonant_length": None,
        "vowel": "a",
        "vowel_length": 0.12,
        "pitch": 5.0
    }

def pause():
    return {
        "text": "、",
        "consonant": None,
        "consonant_length": None,
        "vowel": "pau",
        "vowel_length": 0.25,
        "pitch": 0.0
    }

def phrase(moras, has_pause):
    return {
        "moras": [mora(x) for x in moras],
        "accent": 1,
        "pause_mora": pause() if has_pause else None,
        "is_interrogative": False
    }

def reading_for(text):
    table = {
        "東京": "トウキョウ",
        "東京大学": "トウキョウダイガク",
        "トウキョウ": "トウキョウ",
        "トウキョウダイガク": "トウキョウダイガク",
        "トウキョーダイガク": "トウキョーダイガク",
    }
    return table.get(text, text)

def phrases_for(text):
    reading = reading_for(text)
    if reading == "トウキョウダイガク":
        return [
            phrase(["ト", "ウ", "キョ", "ウ"], True),
            phrase(["ダ", "イ", "ガ", "ク"], False),
        ]
    if reading == "トウキョウ":
        return [phrase(["ト", "ウ", "キョ", "ウ"], False)]
    return [phrase(list(reading), False)]

def query_for(text=""):
    return {
        "accent_phrases": [],
        "speedScale": 1.0,
        "pitchScale": 0.0,
        "intonationScale": 1.0,
        "volumeScale": 1.0,
        "prePhonemeLength": 0.1,
        "postPhonemeLength": 0.1,
        "outputSamplingRate": 24000,
        "outputStereo": False,
        "kana": reading_for(text),
        "pauseLength": None,
        "pauseLengthScale": 1.0
    }

def wav_for_synthesis(raw):
    corrected = False
    try:
        body = json.loads(raw.decode("utf-8"))
        for p in body.get("accent_phrases", []):
            pm = p.get("pause_mora")
            if pm is not None and float(pm.get("vowel_length", -1)) == 0.0:
                corrected = True
                break
    except Exception:
        pass

    frames = 2700 if corrected else 2400
    sample = b"\x01\x00" if corrected else b"\x00\x00"
    bio = BytesIO()
    with wave.open(bio, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(24000)
        w.writeframes(sample * frames)
    return bio.getvalue(), corrected

class Handler(BaseHTTPRequestHandler):
    def _body(self):
        n = int(self.headers.get("Content-Length", "0"))
        return self.rfile.read(n) if n else b""

    def _json(self, value, code=200):
        data = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        log({"method": "GET", "path": parsed.path, "query": query})

        if parsed.path == "/version":
            self._json("0.0.0-vqa")
        elif parsed.path == "/speakers":
            self._json([{
                "name": "VQA Native Smoke",
                "speaker_uuid": "11111111-1111-1111-1111-111111111111",
                "styles": [{"name": "Normal", "id": 1, "type": "talk"}],
                "version": "0.0.0",
                "supported_features": {"permitted_synthesis_morphing": "SELF_ONLY"}
            }])
        elif parsed.path == "/speaker_info":
            self._json({
                "policy": "",
                "portrait": "",
                "style_infos": [{
                    "id": 1,
                    "icon": "",
                    "portrait": None,
                    "voice_samples": ["", "", ""]
                }]
            })
        elif parsed.path == "/is_initialized_speaker":
            self._json(True)
        elif parsed.path == "/engine_manifest":
            self._json({
                "manifest_version": "0.13.1",
                "name": "VQA Native Smoke",
                "brand_name": "VQA",
                "uuid": "00000000-0000-0000-0000-000000000001",
                "version": "0.0.0",
                "url": "https://example.invalid",
                "command": "",
                "port": args.port,
                "icon": "",
                "default_sampling_rate": 24000,
                "frame_rate": 93.75,
                "terms_of_service": "",
                "update_infos": [],
                "dependency_licenses": [],
                "supported_features": {}
            })
        else:
            self._json({})

    def do_POST(self):
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        raw = self._body()
        body = raw.decode("utf-8", errors="replace")
        log({"method": "POST", "path": parsed.path, "query": query, "body": body})

        text = query.get("text", [""])[0]

        if parsed.path == "/audio_query":
            self._json(query_for(text))
            return

        if parsed.path == "/accent_phrases":
            self._json(phrases_for(text))
            return

        if parsed.path == "/initialize_speaker":
            self._json({})
            return

        if parsed.path == "/synthesis":
            wav, corrected = wav_for_synthesis(raw)
            delay_flag = os.path.join(args.output, "delay-next-corrected")
            if corrected and os.path.exists(delay_flag):
                os.unlink(delay_flag)
                with open(os.path.join(args.output, "corrected-request-waiting"), "w") as f:
                    f.write("waiting")
                time.sleep(1.0)
            log({
                "method": "OBSERVE",
                "path": "/synthesis-result",
                "corrected": corrected,
                "wav_length": len(wav)
            })
            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(wav)))
            self.end_headers()
            self.wfile.write(wav)
            return

        self._json({})

    def log_message(self, format, *args):
        pass

server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
with open(os.path.join(args.output, "fake-server-ready.txt"), "w", encoding="utf-8") as f:
    f.write(str(args.port))
server.serve_forever()
