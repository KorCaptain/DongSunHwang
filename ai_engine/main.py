"""
Discord Real-Time Voice Translator - Python AI Engine
Communicates with the C# host via stdin/stdout JSON protocol.

Protocol (newline-delimited JSON):
  C# -> Python  : {"type": "audio", "data": "<base64 pcm float32 16kHz>", "source_lang": "en", "target_lang": "ko"}
  C# -> Python  : {"type": "config", ...}
  C# -> Python  : {"type": "shutdown"}
  Python -> C#  : {"type": "result", "text": "...", "translation": "..."}
  Python -> C#  : {"type": "error", "message": "..."}
  Python -> C#  : {"type": "ready"}
"""

import sys
import json
import base64
import logging
import numpy as np
from pathlib import Path

# Resolve project root and load config
ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = ROOT / "config" / "settings.json"

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    stream=sys.stderr,
)
logger = logging.getLogger("ai_engine.main")


def load_config() -> dict:
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        logger.warning(f"Config file not found at {CONFIG_PATH}, using defaults.")
        return {}


def send(obj: dict):
    """Write a JSON message to stdout (C# reads from here)."""
    line = json.dumps(obj, ensure_ascii=False)
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def send_error(msg: str):
    send({"type": "error", "message": msg})


def decode_audio(b64: str) -> np.ndarray:
    """Decode base64-encoded PCM float32 bytes to numpy array."""
    raw = base64.b64decode(b64)
    audio = np.frombuffer(raw, dtype=np.float32).copy()
    return audio


def main():
    cfg = load_config()
    ai_cfg = cfg.get("ai_engine", {})

    # Model configuration
    whisper_size = ai_cfg.get("whisper_model_size", "medium")
    whisper_path = ai_cfg.get("whisper_model_path", None)
    nllb_name = ai_cfg.get("nllb_model_name", "facebook/nllb-200-distilled-600M")
    nllb_path = ai_cfg.get("nllb_model_path", None)
    vad_threshold = float(ai_cfg.get("vad_threshold", 0.5))
    default_source_lang = ai_cfg.get("source_lang", "en")
    default_target_lang = ai_cfg.get("target_lang", "ko")

    logger.info("Initializing AI components...")

    try:
        from vad import SileroVAD
        from stt import WhisperSTT
        from translate import NLLBTranslator

        vad = SileroVAD(threshold=vad_threshold)
        stt = WhisperSTT(
            model_size=whisper_size,
            model_path=whisper_path,
            language=default_source_lang,
        )
        translator = NLLBTranslator(
            model_name=nllb_name,
            model_path=nllb_path,
        )
    except Exception as e:
        send_error(f"Initialization failed: {e}")
        sys.exit(1)

    send({"type": "ready"})
    logger.info("AI Engine ready. Waiting for audio input...")

    # Audio buffer for accumulating speech segments
    audio_buffer: list[np.ndarray] = []
    is_speaking = False

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            msg = json.loads(line)
        except json.JSONDecodeError as e:
            send_error(f"Invalid JSON: {e}")
            continue

        msg_type = msg.get("type")

        if msg_type == "shutdown":
            logger.info("Shutdown requested.")
            break

        elif msg_type == "config":
            # Dynamic config update
            if "source_lang" in msg:
                default_source_lang = msg["source_lang"]
            if "target_lang" in msg:
                default_target_lang = msg["target_lang"]
            if "vad_threshold" in msg:
                vad.threshold = float(msg["vad_threshold"])
            logger.info(f"Config updated: {msg}")

        elif msg_type == "audio":
            try:
                audio = decode_audio(msg["data"])
                source_lang = msg.get("source_lang", default_source_lang)
                target_lang = msg.get("target_lang", default_target_lang)

                speech_detected = vad.is_speech(audio)

                if speech_detected:
                    audio_buffer.append(audio)
                    is_speaking = True
                elif is_speaking:
                    # End of utterance: process buffered audio
                    is_speaking = False
                    if audio_buffer:
                        full_audio = np.concatenate(audio_buffer)
                        audio_buffer.clear()
                        vad.reset()

                        # STT
                        text = stt.transcribe(full_audio, language=source_lang)
                        if not text:
                            continue

                        # Translation
                        translation = translator.translate(
                            text, source_lang=source_lang, target_lang=target_lang
                        )

                        send({
                            "type": "result",
                            "text": text,
                            "translation": translation,
                            "source_lang": source_lang,
                            "target_lang": target_lang,
                        })

            except Exception as e:
                logger.exception("Error processing audio chunk")
                send_error(str(e))

        else:
            send_error(f"Unknown message type: {msg_type!r}")

    logger.info("AI Engine exiting.")


if __name__ == "__main__":
    main()
