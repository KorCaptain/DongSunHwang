"""
Speech-to-Text using faster-whisper
"""

import numpy as np
import logging
from pathlib import Path
from faster_whisper import WhisperModel

logger = logging.getLogger(__name__)


class WhisperSTT:
    def __init__(
        self,
        model_size: str = "medium",
        model_path: str | None = None,
        device: str = "auto",
        compute_type: str = "auto",
        language: str | None = None,
    ):
        self.language = language
        self._load_model(model_size, model_path, device, compute_type)

    def _load_model(
        self,
        model_size: str,
        model_path: str | None,
        device: str,
        compute_type: str,
    ):
        try:
            # Use local path if provided, otherwise download by size name
            model_id = model_path if model_path and Path(model_path).exists() else model_size

            # Auto-select device and compute type
            import torch
            if device == "auto":
                device = "cuda" if torch.cuda.is_available() else "cpu"
            if compute_type == "auto":
                compute_type = "float16" if device == "cuda" else "int8"

            self.model = WhisperModel(model_id, device=device, compute_type=compute_type)
            logger.info(f"Whisper model '{model_id}' loaded on {device} ({compute_type})")
        except Exception as e:
            logger.error(f"Failed to load Whisper model: {e}")
            raise

    # Whisper가 반환하는 언어 코드 → 앱 내부 코드 변환 맵
    # (Whisper는 ISO 639-1 코드를 사용하지만 일부 불일치 존재)
    _WHISPER_LANG_REMAP: dict[str, str] = {
        "zh": "zh",  # 중국어
        "pt": "pt",  # 포르투갈어-BR
    }

    def transcribe(self, audio: np.ndarray, language: str | None = None) -> str:
        """
        Transcribe audio to text.
        audio: numpy array of float32, shape (N,), values in [-1, 1], 16kHz
        language: ISO 639-1 코드 또는 None/"auto" (자동감지)
        Returns transcribed text string.
        """
        result = self.transcribe_with_info(audio, language)
        return result["text"]

    def transcribe_with_info(self, audio: np.ndarray, language: str | None = None) -> dict:
        """
        Transcribe audio and return text with metadata.
        language: ISO 639-1 코드, None, 또는 "auto" → None으로 처리 (Whisper 자동감지)
        """
        # "auto" 또는 None → Whisper 자체 자동감지
        lang = None if (language is None or language == "auto") else language
        if lang is None:
            logger.debug("Source language: auto-detect (Whisper)")

        segments_list, info = self.model.transcribe(
            audio,
            language=lang,
            beam_size=5,
            vad_filter=False,
            word_timestamps=False,
        )

        segments = list(segments_list)
        text = " ".join(s.text.strip() for s in segments)
        detected = self._WHISPER_LANG_REMAP.get(info.language, info.language)

        logger.debug(
            f"Detected: {detected} (prob={info.language_probability:.2f}), text: {text!r}"
        )

        return {
            "text": text.strip(),
            "language": detected,
            "language_probability": info.language_probability,
            "duration": info.duration,
        }
