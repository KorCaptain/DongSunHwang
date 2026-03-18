"""
Translation using Meta NLLB (No Language Left Behind)
"""

import logging
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
import torch

logger = logging.getLogger(__name__)

# NLLB language code map
# 핵심 7개 언어 + 추가 언어
LANG_CODE_MAP = {
    # ── 핵심 7개 언어 ───────────────────────────────────────────
    "ko": "kor_Hang",   # 한국어
    "en": "eng_Latn",   # 영어
    "de": "deu_Latn",   # 독일어
    "ja": "jpn_Jpan",   # 일본어
    "ru": "rus_Cyrl",   # 러시아어
    "pt": "por_Latn",   # 포르투갈어 (브라질 포함, NLLB 코드 동일)
    "zh": "zho_Hans",   # 중국어 (간체)
    # ── 추가 언어 (향후 확장용) ──────────────────────────────────
    "fr": "fra_Latn",   # 프랑스어
    "es": "spa_Latn",   # 스페인어
    "ar": "arb_Arab",   # 아랍어
    "vi": "vie_Latn",   # 베트남어
    "th": "tha_Thai",   # 태국어
    "id": "ind_Latn",   # 인도네시아어
    "zh-tw": "zho_Hant", # 중국어 (번체)
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
