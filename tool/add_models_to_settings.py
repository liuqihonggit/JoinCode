"""给 settings.json 的空供应商添加真实模型列表"""
import json, os, copy

path = os.path.expanduser('~/.jcc/settings.json')
with open(path, 'r', encoding='utf-8') as f:
   settings = json.load(f)

vendors = settings.get('vendor', {})

# 模型模板
def make_model(model_id, display_name, context_window=128000, fast_mode=True):
    return {
        "id": model_id,
        "canonicalId": model_id,
        "displayName": display_name,
        "contextWindow": context_window,
        "description": "",
        "aliases": [],
        "capabilities": {
            "fastMode": fast_mode,
            "effort": False,
            "maxEffort": False,
            "thinkingMode": False,
            "modalities": ["text"]
        },
        "pricing": None,
        "knowledgeCutoff": None
    }

# OpenAI 模型列表
openai_models = [
    ("gpt-4o-mini", "GPT-4o Mini", 128000),
    ("gpt-4o", "GPT-4o", 128000),
    ("gpt-4.1-nano", "GPT-4.1 Nano", 128000),
    ("gpt-4.1-mini", "GPT-4.1 Mini", 128000),
    ("gpt-4.1", "GPT-4.1", 128000),
    ("o4-mini", "o4 Mini", 200000),
    ("o3-mini", "o3 Mini", 200000),
    ("o3", "o3", 200000),
    ("gpt-5.6-sol", "GPT-5.6 Sol", 400000),
    ("gpt-5.6-terra", "GPT-5.6 Terra", 400000),
    ("gpt-5.6-luna", "GPT-5.6 Luna", 400000),
    ("gpt-5.4", "GPT-5.4", 400000),
    ("gpt-5.4-mini", "GPT-5.4 Mini", 400000),
    ("gpt-5.4-nano", "GPT-5.4 Nano", 400000),
    ("gpt-5.4-image-2", "GPT-5.4 Image 2", 400000),
    ("gpt-audio", "GPT Audio", 128000),
    ("gpt-audio-mini", "GPT Audio Mini", 128000),
    ("o3-pro", "o3 Pro", 200000),
]

# Anthropic 模型列表
anthropic_models = [
    ("claude-mythos-5", "Claude Mythos 5", 200000),
    ("claude-opus-5", "Claude Opus 5", 200000),
    ("claude-sonnet-5", "Claude Sonnet 5", 200000),
    ("claude-fable-5", "Claude Fable 5", 200000),
    ("claude-opus-4-8", "Claude Opus 4.8", 200000),
    ("claude-opus-4-7", "Claude Opus 4.7", 200000),
    ("claude-opus-4-6", "Claude Opus 4.6", 200000),
    ("claude-sonnet-4-6", "Claude Sonnet 4.6", 200000),
    ("claude-sonnet-4-5", "Claude Sonnet 4.5", 200000),
    ("claude-opus-4-5", "Claude Opus 4.5", 200000),
    ("claude-haiku-4-5", "Claude Haiku 4.5", 200000),
]

# DeepSeek 模型列表
deepseek_models = [
    ("deepseek-v4-flash", "DeepSeek V4 Flash", 1048576),
    ("deepseek-v4-pro", "DeepSeek V4 Pro", 1048576),
]

# Zhipu 模型列表
zhipu_models = [
    ("glm-5.2", "GLM-5.2", 1048576),
    ("glm-4-flash", "GLM-4 Flash", 128000),
    ("glm-4", "GLM-4", 128000),
    ("glm-4v", "GLM-4V", 128000),
    ("glm-4-air", "GLM-4 Air", 128000),
    ("glm-4-airx", "GLM-4 AirX", 128000),
    ("glm-4-long", "GLM-4 Long", 1048576),
]

# 更新空供应商
for vendor_name, model_list, default_model in [
    ("openai", openai_models, "gpt-5.6-sol"),
    ("anthropic", anthropic_models, "claude-sonnet-5"),
    ("deepseek", deepseek_models, "deepseek-v4-pro"),
    ("zhipu", zhipu_models, "glm-5.2"),
]:
    if vendor_name not in vendors:
        print(f"  跳过 {vendor_name}: 不在 settings.json 中")
        continue
    existing_models = vendors[vendor_name].get("models", [])
    if existing_models:
        print(f"  跳过 {vendor_name}: 已有 {len(existing_models)} 个模型")
        continue
    vendors[vendor_name]["models"] = [make_model(mid, name, ctx) for mid, name, ctx in model_list]
    vendors[vendor_name]["model"] = default_model
    print(f"  ✅ {vendor_name}: 添加 {len(model_list)} 个模型, 默认={default_model}")

# 写回
with open(path, 'w', encoding='utf-8') as f:
    json.dump(settings, f, ensure_ascii=False, indent=2)

print("\nsettings.json 已更新")
