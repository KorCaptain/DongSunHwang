"""
Discord Real-Time Voice Translator - Python AI Engine
C# 호스트와 stdin/stdout JSON 프로토콜로 통신합니다.

프로토콜 (줄바꿈 구분 JSON):
  C# → Python : {"type": "audio", "data": "<base64 PCM float32 16kHz>",
                  "source_lang": "en|auto", "target_lang": "ko"}
  C# → Python : {"type": "config", "source_lang": "...", "target_lang": "...",
                  "vad_threshold": 0.5}
  C# → Python : {"type": "shutdown"}

  Python → C# : {"type": "ready"}
  Python → C# : {"type": "result", "text": "...", "translation": "...",
                  "source_lang": "en", "target_lang": "ko",
                  "detected_lang": "en"}   ← source_lang="auto"일 때 실제 감지 언어
  Python → C# : {"type": "error", "message": "..."}
"""

import sys
import json
import base64
import logging
import numpy as np
from pathlib import Path

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
        logger.warning(f"Config not found at {CONFIG_PATH}, using defaults.")
        return {}


def send(obj: dict):
    """stdout으로 JSON 메시지 전송 (C#이 읽음)."""
    sys.stdout.write(json.dumps(obj, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def send_error(msg: str):
    send({"type": "error", "message": msg})


def decode_audio(b64: str) -> np.ndarray:
    """Base64 인코딩된 PCM float32 bytes → numpy array."""
    raw = base64.b64decode(b64)
    return np.frombuffer(raw, dtype=np.float32).copy()


def main():
    cfg = load_config()
    ai_cfg = cfg.get("ai_engine", {})

    whisper_size  = ai_cfg.get("whisper_model_size", "medium")
    whisper_path  = ai_cfg.get("whisper_model_path", None)
    nllb_name     = ai_cfg.get("nllb_model_name", "facebook/nllb-200-distilled-600M")
    nllb_path     = ai_cfg.get("nllb_model_path", None)
    vad_threshold = float(ai_cfg.get("vad_threshold", 0.5))

    # 기본 언어 설정 ("auto" 허용)
    default_source_lang = cfg.get("source_lang", "auto")
    default_target_lang = cfg.get("target_lang", "ko")

    logger.info("AI 컴포넌트 초기화 중...")

    try:
        from vad import SileroVAD
        from stt import WhisperSTT
        from translate import NLLBTranslator

        vad = SileroVAD(threshold=vad_threshold)
        stt = WhisperSTT(
            model_size=whisper_size,
            model_path=whisper_path,
            # language=None → Whisper 자동감지 모드로 로드
            language=None if default_source_lang == "auto" else default_source_lang,
        )
        translator = NLLBTranslator(model_name=nllb_name, model_path=nllb_path)
    except Exception as e:
        send_error(f"초기화 실패: {e}")
        sys.exit(1)

    send({"type": "ready"})
    logger.info("AI Engine 준비 완료. 오디오 입력 대기 중...")

    audio_buffer: list[np.ndarray] = []
    is_speaking = False

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            msg = json.loads(line)
        except json.JSONDecodeError as e:
            send_error(f"JSON 파싱 오류: {e}")
            continue

        msg_type = msg.get("type")

        # ── shutdown ─────────────────────────────────────────────────────────
        if msg_type == "shutdown":
            logger.info("종료 요청.")
            break

        # ── config ───────────────────────────────────────────────────────────
        elif msg_type == "config":
            if "source_lang" in msg:
                default_source_lang = msg["source_lang"]
            if "target_lang" in msg:
                default_target_lang = msg["target_lang"]
            if "vad_threshold" in msg:
                vad.threshold = float(msg["vad_threshold"])
            logger.info(f"설정 업데이트: source={default_source_lang}, "
                        f"target={default_target_lang}, vad={vad.threshold}")

        # ── audio ────────────────────────────────────────────────────────────
        elif msg_type == "audio":
            try:
                audio = decode_audio(msg["data"])
                source_lang = msg.get("source_lang", default_source_lang)
                target_lang = msg.get("target_lang", default_target_lang)
                auto_detect = (source_lang == "auto")

                speech_detected = vad.is_speech(audio)

                if speech_detected:
                    audio_buffer.append(audio)
                    is_speaking = True
                elif is_speaking:
                    # 발화 종료 → 누적 버퍼 처리
                    is_speaking = False
                    if not audio_buffer:
                        continue

                    full_audio = np.concatenate(audio_buffer)
                    audio_buffer.clear()
                    vad.reset()

                    # STT (auto 모드면 language=None → Whisper 자동감지)
                    stt_result = stt.transcribe_with_info(
                        full_audio,
                        language=None if auto_detect else source_lang,
                    )

                    text = stt_result["text"]
                    if not text:
                        continue

                    # 실제 감지된 언어 코드
                    detected_lang = stt_result["language"]
                    effective_source = detected_lang if auto_detect else source_lang

                    if auto_detect:
                        logger.info(f"자동감지 언어: {detected_lang} "
                                    f"(확률={stt_result['language_probability']:.2f})")

                    # 번역 (같은 언어면 원문 그대로)
                    if effective_source == target_lang:
                        translation = text
                    else:
                        translation = translator.translate(
                            text,
                            source_lang=effective_source,
                            target_lang=target_lang,
                        )

                    send({
                        "type": "result",
                        "text": text,
                        "translation": translation,
                        "source_lang": effective_source,
                        "target_lang": target_lang,
                        # auto 모드에서 감지된 언어를 C#에 알림
                        "detected_lang": detected_lang if auto_detect else None,
                    })

            except Exception as e:
                logger.exception("오디오 처리 중 오류")
                send_error(str(e))

        else:
            send_error(f"알 수 없는 메시지 타입: {msg_type!r}")

    logger.info("AI Engine 종료.")


if __name__ == "__main__":
    main()
