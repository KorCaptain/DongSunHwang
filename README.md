# Discord Real-Time Voice Translator

Discord 음성을 실시간으로 번역하여 오버레이 자막으로 출력하는 도구입니다.

```
Discord 음성
   ↓
[Audio Capture]  (WASAPI Loopback / Microphone)
   ↓
[Python AI Engine]
   ├─ Silero VAD  → 음성 구간 감지
   ├─ faster-whisper → 음성→텍스트 (STT)
   └─ NLLB (Meta) → 텍스트 번역
   ↓
[Overlay 자막]  (C# WinForms, 투명 항상 위)
```

## 프로젝트 구조

```
project_root/
├── app/                      # C# WinForms 프로그램
│   ├── Audio/
│   │   ├── AudioCapture.cs   # WASAPI Loopback / Mic 캡처 + 리샘플링
│   │   └── AiEngineClient.cs # Python 서브프로세스 IPC (JSON over stdio)
│   ├── UI/
│   │   ├── MainForm.cs       # 메인 컨트롤 패널
│   │   └── SettingsModel.cs  # 설정 데이터 모델
│   ├── Overlay/
│   │   └── SubtitleOverlay.cs # 투명 자막 오버레이 창
│   ├── Program.cs
│   └── DiscordVoiceTranslator.csproj
├── ai_engine/                # Python AI 엔진
│   ├── vad.py                # Silero VAD
│   ├── stt.py                # faster-whisper STT
│   ├── translate.py          # NLLB 번역
│   ├── main.py               # 메인 루프 (JSON stdio 프로토콜)
│   └── requirements.txt
├── models/
│   ├── whisper/              # Whisper 모델 파일 (선택)
│   └── nllb/                 # NLLB 모델 파일 (선택)
├── config/
│   └── settings.json         # 언어, 모델, UI 설정
└── .gitignore
```

## 요구 사항

### C# (UI)
- .NET 8 SDK (Windows)
- NuGet: `NAudio`, `Newtonsoft.Json`

### Python (AI 엔진)
- Python 3.10+
- CUDA 지원 GPU 권장 (CPU 동작 가능)

```bash
pip install -r ai_engine/requirements.txt
```

## 빠른 시작

### 1. Python 의존성 설치

```bash
pip install -r ai_engine/requirements.txt
```

### 2. 설정 조정

`config/settings.json` 에서 언어, 모델 크기 등을 설정합니다.

```json
{
  "source_lang": "en",
  "target_lang": "ko",
  "capture_mode": "Loopback",
  "ai_engine": {
    "python_exe": "python",
    "whisper_model_size": "medium",
    "nllb_model_name": "facebook/nllb-200-distilled-600M",
    "vad_threshold": 0.5
  }
}
```

| 옵션 | 설명 |
|------|------|
| `source_lang` | 입력 언어 ISO 코드 (en, ko, ja, zh ...) |
| `target_lang` | 출력 언어 ISO 코드 |
| `capture_mode` | `"Loopback"` (시스템 오디오) / `"Microphone"` |
| `whisper_model_size` | `tiny` / `base` / `small` / `medium` / `large-v3` |
| `nllb_model_name` | HuggingFace 모델 ID (distilled-600M 권장) |
| `vad_threshold` | 음성 감지 민감도 0.0~1.0 (기본 0.5) |

### 3. C# 빌드 및 실행

```bash
cd app
dotnet build
dotnet run
```

또는 Visual Studio / Rider에서 `app/DiscordVoiceTranslator.csproj` 열기.

### 4. 사용법

1. **Source Language**: Discord에서 상대방이 사용하는 언어
2. **Target Language**: 번역될 언어 (자막으로 표시)
3. **Capture Mode**: `System Audio` - Discord 출력 음성 캡처
4. **▶ Start** 클릭 → 오버레이 자막이 화면 하단에 표시됨
5. 오버레이 창은 드래그로 위치 이동 가능

## IPC 프로토콜 (C# ↔ Python)

JSON을 줄바꿈 구분으로 stdin/stdout으로 주고받습니다.

**C# → Python**
```json
{"type": "audio", "data": "<base64 PCM float32 16kHz>", "source_lang": "en", "target_lang": "ko"}
{"type": "config", "source_lang": "ja", "target_lang": "ko", "vad_threshold": 0.6}
{"type": "shutdown"}
```

**Python → C#**
```json
{"type": "ready"}
{"type": "result", "text": "Hello world", "translation": "안녕 세상", "source_lang": "en", "target_lang": "ko"}
{"type": "error", "message": "..."}
```

## 지원 언어

| 코드 | 언어 |
|------|------|
| en | English |
| ko | Korean |
| ja | Japanese |
| zh | Chinese (Simplified) |
| de | German |
| fr | French |
| es | Spanish |
| ru | Russian |
| ar | Arabic |
| vi | Vietnamese |
| th | Thai |
| id | Indonesian |

## 로컬 모델 사용

모델을 직접 다운로드해서 사용하려면 `settings.json`에서 경로를 지정합니다.

```json
{
  "ai_engine": {
    "whisper_model_path": "models/whisper/medium",
    "nllb_model_path": "models/nllb/nllb-200-distilled-600M"
  }
}
```

HuggingFace CLI로 모델 다운로드:
```bash
huggingface-cli download facebook/nllb-200-distilled-600M --local-dir models/nllb/nllb-200-distilled-600M
```
