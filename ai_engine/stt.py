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

    def transcribe(self, audio: np.ndarray, language: str | None = None) -> str:
        """
        Transcribe audio to text.
        audio: numpy array of float32, shape (N,), values in [-1, 1], 16kHz
        Returns transcribed text string.
        """
        lang = language or self.language

        segments, info = self.model.transcribe(
            audio,
            language=lang,
            beam_size=5,
            vad_filter=False,  # VAD handled externally
            word_timestamps=False,
        )

        text = " ".join(segment.text.strip() for segment in segments)
        detected_lang = info.language
        logger.debug(f"Detected language: {detected_lang}, text: {text}")
        return text.strip()

    def transcribe_with_info(self, audio: np.ndarray, language: str | None = None) -> dict:
        """
        Transcribe audio and return text with metadata.
        """
        lang = language or self.language
        segments_list, info = self.model.transcribe(
            audio,
            language=lang,
            beam_size=5,
            vad_filter=False,
            word_timestamps=False,
        )

        segments = list(segments_list)
        text = " ".join(s.text.strip() for s in segments)

        return {
            "text": text.strip(),
            "language": info.language,
            "language_probability": info.language_probability,
            "duration": info.duration,
        }
