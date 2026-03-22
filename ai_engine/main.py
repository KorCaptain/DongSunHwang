"""
Discord Real-Time Voice Translator - Python AI Engine
C# 호스트와 stdin/stdout JSON 프로토콜로 통신합니다.

프로토콜 (줄바꿈 구분 JSON):
  C# → Python : {"type": "audio", "data": "<base64 PCM float32 16kHz>",
                  "source_lang": "en|auto", "target_lang": "ko"}
  C# → Python : {"type": "config", "source_lang": "...", "target_lang": "...",
                  "vad_threshold": 0.5, "translation_engine": "argos|nllb"}
  C# → Python : {"type": "translate_text", "text": "...", "source_lang": "...",
                  "target_lang": "...", "request_id": "...", "author": "..."}
  C# → Python : {"type": "shutdown"}

  Python → C# : {"type": "ready"}
  Python → C# : {"type": "result", "text": "...", "translation": "...",
                  "source_lang": "en", "target_lang": "ko",
                  "detected_lang": "en"}
  Python → C# : {"type": "text_result", "text": "...", "translation": "...",
                  "source_lang": "...", "target_lang": "...",
                  "request_id": "...", "author": "..."}
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


# ── 번역 엔진 팩토리 ─────────────────────────────────────────────────────────

def make_translator(engine_name: str, nllb_name: str, nllb_path):
    """번역 엔진 인스턴스를 생성합니다."""
    if engine_name == "argos":
        return ArgosTranslator()
    else:
        from translate import NLLBTranslator
        return NLLBTranslator(model_name=nllb_name, model_path=nllb_path)


class ArgosTranslator:
    """argostranslate를 이용한 빠른 번역 엔진 (오프라인)."""

    # NLLB 언어 코드 → argostranslate 언어 코드 매핑
    LANG_MAP = {
        "ko": "ko", "en": "en", "de": "de", "ja": "ja",
        "ru": "ru", "pt": "pt", "zh": "zh",
    }

    def __init__(self):
        try:
            import argostranslate.package
            import argostranslate.translate
            self._pkg = argostranslate.package
            self._tr = argostranslate.translate
            logger.info("argostranslate 초기화 완료")
        except ImportError:
            raise RuntimeError(
                "argostranslate가 설치되지 않았습니다. "
                "pip install argostranslate 를 실행하세요."
            )

    def _code(self, lang: str) -> str:
        """언어 코드 정규화."""
        # NLLB 스타일(kor_Hang 등)을 2글자로 축약
        if "_" in lang:
            lang = lang[:3]
        # 3글자 ISO → 2글자
        _iso3 = {"kor": "ko", "eng": "en", "deu": "de", "jpn": "ja",
                 "rus": "ru", "por": "pt", "zho": "zh"}
        lang = _iso3.get(lang, lang)
        return self.LANG_MAP.get(lang, lang)

    def translate(self, text: str, source_lang: str, target_lang: str) -> str:
        src = self._code(source_lang)
        tgt = self._code(target_lang)
        if src == tgt:
            return text
        try:
            installed = self._tr.get_installed_languages()
            src_lang_obj = next((l for l in installed if l.code == src), None)
            if src_lang_obj is None:
                # 패키지 자동 설치 시도
                self._install_package(src, tgt)
                installed = self._tr.get_installed_languages()
                src_lang_obj = next((l for l in installed if l.code == src), None)
            if src_lang_obj is None:
                return f"[argos: {src}→{tgt} 패키지 없음] {text}"
            translation = src_lang_obj.get_translation(tgt)
            if translation is None:
                self._install_package(src, tgt)
                translation = src_lang_obj.get_translation(tgt)
            if translation is None:
                return f"[argos: {src}→{tgt} 번역 실패] {text}"
            return translation.translate(text)
        except Exception as e:
            logger.warning(f"argos translate error: {e}")
            return text

    def _install_package(self, src: str, tgt: str):
        """필요한 argostranslate 패키지를 자동 다운로드합니다."""
        try:
            self._pkg.update_package_index()
            available = self._pkg.get_available_packages()
            pkg = next(
                (p for p in available if p.from_code == src and p.to_code == tgt),
                None
            )
            if pkg:
                logger.info(f"argostranslate 패키지 설치 중: {src}→{tgt}")
                self._pkg.install_from_path(pkg.download())
        except Exception as e:
            logger.warning(f"argostranslate 패키지 설치 실패: {e}")


# ─────────────────────────────────────────────────────────────────────────────

def main():
    cfg = load_config()
    ai_cfg = cfg.get("ai_engine", {})

    whisper_size  = ai_cfg.get("whisper_model_size", "medium")
    whisper_path  = ai_cfg.get("whisper_model_path", None)
    nllb_name     = ai_cfg.get("nllb_model_name", "facebook/nllb-200-distilled-600M")
    nllb_path     = ai_cfg.get("nllb_model_path", None)
    vad_threshold = float(ai_cfg.get("vad_threshold", 0.5))

    default_source_lang = cfg.get("source_lang", "auto")
    default_target_lang = cfg.get("target_lang", "ko")
    translation_engine  = cfg.get("translation_engine", "argos")  # "argos" or "nllb"

    logger.info("AI 컴포넌트 초기화 중...")

    try:
        from vad import SileroVAD
        from stt import WhisperSTT

        vad = SileroVAD(threshold=vad_threshold)
        stt = WhisperSTT(
            model_size=whisper_size,
            model_path=whisper_path,
            language=None if default_source_lang == "auto" else default_source_lang,
        )
        translator = make_translator(translation_engine, nllb_name, nllb_path)
    except Exception as e:
        send_error(f"초기화 실패: {e}")
        sys.exit(1)

    send({"type": "ready"})
    logger.info(f"AI Engine 준비 완료. 번역 엔진: {translation_engine}")

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
            if "translation_engine" in msg:
                new_engine = msg["translation_engine"]
                if new_engine != translation_engine:
                    translation_engine = new_engine
                    try:
                        translator = make_translator(translation_engine, nllb_name, nllb_path)
                        logger.info(f"번역 엔진 변경됨: {translation_engine}")
                    except Exception as e:
                        send_error(f"번역 엔진 변경 실패: {e}")
            logger.info(f"설정 업데이트: src={default_source_lang}, tgt={default_target_lang}, "
                        f"vad={vad.threshold}, engine={translation_engine}")

        # ── translate_text (발헤임 채팅 수신 번역) ───────────────────────────
        elif msg_type == "translate_text":
            text        = msg.get("text", "")
            source_lang = msg.get("source_lang", "auto")
            target_lang = msg.get("target_lang", default_target_lang)
            request_id  = msg.get("request_id", "")
            author      = msg.get("author", "")

            if not text:
                continue
            try:
                # source_lang == "auto"면 번역 엔진이 자동 감지 (NLLB는 지원 안 하므로 영어 가정)
                effective_src = source_lang if source_lang != "auto" else "en"
                if effective_src == target_lang:
                    translation = text
                else:
                    translation = translator.translate(
                        text,
                        source_lang=effective_src,
                        target_lang=target_lang,
                    )
                send({
                    "type": "text_result",
                    "text": text,
                    "translation": translation,
                    "source_lang": effective_src,
                    "target_lang": target_lang,
                    "request_id": request_id,
                    "author": author,
                })
            except Exception as e:
                logger.exception("텍스트 번역 오류")
                send_error(str(e))

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
                    is_speaking = False
                    if not audio_buffer:
                        continue

                    full_audio = np.concatenate(audio_buffer)
                    audio_buffer.clear()
                    vad.reset()

                    stt_result = stt.transcribe_with_info(
                        full_audio,
                        language=None if auto_detect else source_lang,
                    )

                    text = stt_result["text"]
                    if not text:
                        continue

                    detected_lang = stt_result["language"]
                    effective_source = detected_lang if auto_detect else source_lang

                    if auto_detect:
                        logger.info(f"자동감지 언어: {detected_lang} "
                                    f"(확률={stt_result['language_probability']:.2f})")

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
