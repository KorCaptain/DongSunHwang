"""
Voice Activity Detection using Silero VAD
"""

import numpy as np
import torch
import logging

logger = logging.getLogger(__name__)


class SileroVAD:
    def __init__(self, threshold: float = 0.5, sampling_rate: int = 16000):
        self.threshold = threshold
        self.sampling_rate = sampling_rate
        self.model = None
        self.utils = None
        self._load_model()

    def _load_model(self):
        try:
            self.model, self.utils = torch.hub.load(
                repo_or_dir="snakers4/silero-vad",
                model="silero_vad",
                force_reload=False,
                onnx=False,
            )
            self.model.eval()
            logger.info("Silero VAD model loaded successfully")
        except Exception as e:
            logger.error(f"Failed to load Silero VAD: {e}")
            raise

    def is_speech(self, audio_chunk: np.ndarray) -> bool:
        """
        Determine if audio chunk contains speech.
        audio_chunk: numpy array of float32, shape (N,), values in [-1, 1]
        """
        tensor = torch.from_numpy(audio_chunk).float()
        if tensor.dim() == 1:
            tensor = tensor.unsqueeze(0)

        with torch.no_grad():
            speech_prob = self.model(tensor, self.sampling_rate).item()

        return speech_prob >= self.threshold

    def get_speech_timestamps(self, audio: np.ndarray) -> list:
        """
        Get timestamps of speech segments in the audio.
        Returns list of dicts with 'start' and 'end' keys (in samples).
        """
        get_speech_timestamps = self.utils[0]
        tensor = torch.from_numpy(audio).float()

        timestamps = get_speech_timestamps(
            tensor,
            self.model,
            threshold=self.threshold,
            sampling_rate=self.sampling_rate,
        )
        return timestamps

    def reset(self):
        """Reset VAD internal state between utterances."""
        self.model.reset_states()
