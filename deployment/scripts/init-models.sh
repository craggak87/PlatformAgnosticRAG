#!/usr/bin/env bash
# ==============================================================================
# Cell Knowledge Assistant - Local Ollama Model Provisioning Script
# Pulls required models specified in ADR-003, ADR-004, and Doc 8 §7
# ==============================================================================
set -euo pipefail

OLLAMA_HOST="${OLLAMA_BASE_URL:-http://localhost:11434}"

echo "=========================================================="
echo " Cell Knowledge Assistant: Initializing Local LLM Models"
echo " Ollama Endpoint: ${OLLAMA_HOST}"
echo "=========================================================="

# Check if Ollama daemon is reachable
echo "Checking Ollama connectivity..."
if ! curl -sf "${OLLAMA_HOST}/api/tags" > /dev/null 2>&1; then
    echo "ERROR: Unable to connect to Ollama at ${OLLAMA_HOST}."
    echo "Please ensure the Ollama service is running (e.g., 'systemctl status ollama' or 'ollama serve')."
    exit 1
fi

echo "Ollama service is online."
echo ""

# Required models per ADR-003 & ADR-004
MODELS=(
    "gemma4:e4b-it-q4_K_M"      # Primary Reasoning & Chat Model (16GB RAM target)
    "embeddinggemma-2:latest"   # Dense Vector Embeddings (768 dimensions)
    "qwen3.8:latest"            # Fallback Chat Model
)

for MODEL in "${MODELS[@]}"; do
    echo "----------------------------------------------------------"
    echo "Pulling model: ${MODEL} ..."
    ollama pull "${MODEL}"
    echo "Successfully pulled: ${MODEL}"
done

echo ""
echo "=========================================================="
echo " Installed Models in Local Ollama Cache:"
echo "=========================================================="
ollama list

echo ""
echo "Model initialization completed successfully."
