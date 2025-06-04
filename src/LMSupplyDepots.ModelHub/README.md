# LMSupplyDepots.ModelHub

LMSupplyDepots.ModelHub is a .NET library for searching, downloading, and managing local language models.

## Model File Structure

LMSupplyDepots.ModelHub manages model files with a systematic and consistent structure:

```
<data-path>/models/<model-type-dash-case>/<publisher>/<modelName>/{artifact-files...}
<data-path>/models/<model-type-dash-case>/<publisher>/<modelName>/{artifactName}.json  # Metadata

<data-path>/.downloads/{modelId}.download  # Download state files
```

* **model-type-dash-case**: Directory for model type (e.g., `text-generation`, `embedding`, `multimodal`)
* **publisher**: Organization or user that published the model (e.g., `meta`, `mistral`, `local`)
* **modelName**: Name of the model
* **artifact-files**: Model files, could be single or multiple files
* **{artifactName}.json**: Model metadata (created after download completion)
* **{modelId}.download**: Download state files (stored in central `.downloads` directory)

### Supported Model Structures

#### 1. Text Generation Models

```
Example: https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF

Path Structure:
models/text-generation/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/Meta-Llama-3.1-8B-Instruct-IQ2_M.gguf
models/text-generation/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/Meta-Llama-3.1-8B-Instruct-IQ2_M.json
```

**Identifier Analysis:**
- **Registry**: huggingface
- **Publisher**: bartowski
- **ModelName**: Meta-Llama-3.1-8B-Instruct-GGUF
- **ArtifactName**: Meta-Llama-3.1-8B-Instruct-IQ2_M
- **Format**: gguf
- **ModelType**: TextGeneration

#### 2. Embedding Models

```
Example: https://huggingface.co/BAAI/bge-large-en-v1.5

Path Structure:
models/embedding/BAAI/bge-large-en-v1.5/model.safetensors
models/embedding/BAAI/bge-large-en-v1.5/config.json
models/embedding/BAAI/bge-large-en-v1.5/model.json
```

**Identifier Analysis:**
- **Registry**: huggingface
- **Publisher**: BAAI
- **ModelName**: bge-large-en-v1.5
- **ArtifactName**: model
- **Format**: safetensors
- **ModelType**: Embedding

#### 3. Multi-File Models

```
Example: https://huggingface.co/unsloth/DeepSeek-R1-GGUF

Path Structure:
models/text-generation/unsloth/DeepSeek-R1-GGUF/DeepSeek-R1.BF16-00001-of-00030.gguf
models/text-generation/unsloth/DeepSeek-R1-GGUF/DeepSeek-R1.BF16-00002-of-00030.gguf
...
models/text-generation/unsloth/DeepSeek-R1-GGUF/DeepSeek-R1.BF16-00030-of-00030.gguf
models/text-generation/unsloth/DeepSeek-R1-GGUF/DeepSeek-R1.BF16.json
```

**Identifier Analysis:**
- **Registry**: huggingface
- **Publisher**: unsloth
- **ModelName**: DeepSeek-R1-GGUF
- **ArtifactName**: DeepSeek-R1.BF16
- **Format**: gguf
- **ModelType**: TextGeneration

### Model Identifiers

Models are uniquely identified using the format:
```
{registry}:{publisher}/{modelName}/{artifactName}
```

For example:
```
huggingface:bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/Meta-Llama-3.1-8B-Instruct-IQ2_M
```

## Core Components

### Model Management

- **ModelIdentifier**: Value object representing a unique model identifier
- **FileSystemModelRepository**: Implementation of the repository pattern for model storage
- **ModelManager**: High-level service for model operations

### File System Management

- **FileSystemHelper**: Utility for managing model directory and file structures
- **ModelFileStructure**: Representation of model directory structure information
- **ModelMigrationHelper**: Utility for migrating models from old to new directory structure

### Download Management

- **DownloadManager**: Central service for managing model downloads
- **DownloadStateManager**: Manages persistent download state information
- **IModelDownloader**: Interface for model downloaders from different sources

## Key Features

### Model Management

- **Model Search**: Search for models in local storage and external sources (HuggingFace, etc.)
- **Model Download**: Download models from external sources with progress tracking
- **Metadata Management**: Store and retrieve model information
- **Download Control**: Pause, resume, and cancel downloads

### Model Repository

- **File System-Based Storage**: Store models in the local file system
- **Metadata Support**: Store and retrieve model metadata
- **Logical Structure**: Directory structure based on model type, publisher, and name

### Download Management

- **Parallel Downloads**: Support for downloading multiple models and files concurrently
- **Resume Support**: Resume interrupted downloads
- **Status Tracking**: Track download status and progress

## Extensibility

- **Custom Downloaders**: Add support for new model sources by implementing IModelDownloader
- **Dependency Injection**: Flexible service configuration via dependency injection pattern
- **Logging Integration**: Support for various logging providers
