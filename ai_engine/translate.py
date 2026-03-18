"""
Translation using Meta NLLB (No Language Left Behind)
"""

import logging
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
import torch

logger = logging.getLogger(__name__)

# NLLB language code map (common languages)
LANG_CODE_MAP = {
    "ko": "kor_Hang",
    "en": "eng_Latn",
    "ja": "jpn_Jpan",
    "zh": "zho_Hans",
    "de": "deu_Latn",
    "fr": "fra_Latn",
    "es": "spa_Latn",
    "ru": "rus_Cyrl",
    "ar": "arb_Arab",
    "vi": "vie_Latn",
    "th": "tha_Thai",
    "id": "ind_Latn",
}


def get_nllb_lang(lang: str) -> str:
    """Convert ISO 639-1 language code to NLLB language code."""
    return LANG_CODE_MAP.get(lang.lower(), lang)


class NLLBTranslator:
    def __init__(
        self,
        model_name: str = "facebook/nllb-200-distilled-600M",
        model_path: str | None = None,
        device: str = "auto",
        max_length: int = 512,
    ):
        self.max_length = max_length
        self._load_model(model_name, model_path, device)

    def _load_model(self, model_name: str, model_path: str | None, device: str):
        try:
            import torch
            if device == "auto":
                device = "cuda" if torch.cuda.is_available() else "cpu"
            self.device = device

            source = model_path if model_path else model_name
            logger.info(f"Loading NLLB model from '{source}'...")

            self.tokenizer = AutoTokenizer.from_pretrained(source)
            self.model = AutoModelForSeq2SeqLM.from_pretrained(source)
            self.model.to(self.device)
            self.model.eval()

            logger.info(f"NLLB model loaded on {self.device}")
        except Exception as e:
            logger.error(f"Failed to load NLLB model: {e}")
            raise

    def translate(self, text: str, source_lang: str, target_lang: str) -> str:
        """
        Translate text from source_lang to target_lang.
        Accepts ISO 639-1 codes (e.g. 'en', 'ko') or NLLB codes directly.
        """
        if not text.strip():
            return ""

        src_nllb = get_nllb_lang(source_lang)
        tgt_nllb = get_nllb_lang(target_lang)

        inputs = self.tokenizer(
            text,
            return_tensors="pt",
            padding=True,
            truncation=True,
            max_length=self.max_length,
        ).to(self.device)

        forced_bos_token_id = self.tokenizer.convert_tokens_to_ids(tgt_nllb)

        with torch.no_grad():
            output_tokens = self.model.generate(
                **inputs,
                forced_bos_token_id=forced_bos_token_id,
                max_length=self.max_length,
                num_beams=4,
                early_stopping=True,
            )

        translated = self.tokenizer.batch_decode(
            output_tokens, skip_special_tokens=True
        )[0]

        logger.debug(f"[{src_nllb} -> {tgt_nllb}] {text!r} => {translated!r}")
        return translated.strip()
