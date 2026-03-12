"""
Provision knowledge source and knowledge base for the Fault Diagnosis Agent.

Steps:
  1. Create a Blob Storage knowledge source
  2. Create the knowledge base referencing that source
  3. Test the knowledge base with a sample query
  4. Create a project connection to expose the KB as an MCP server
"""

import os
import requests
from dotenv import load_dotenv
from azure.core.credentials import AzureKeyCredential
from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import (
    AzureBlobKnowledgeSource,
    AzureBlobKnowledgeSourceParameters,
    AzureOpenAIVectorizerParameters,
    KnowledgeBase,
    KnowledgeBaseAzureOpenAIModel,
    KnowledgeRetrievalLowReasoningEffort,
    KnowledgeRetrievalOutputMode,
    KnowledgeSourceAzureOpenAIVectorizer,
    KnowledgeSourceContentExtractionMode,
    KnowledgeSourceIngestionParameters,
    KnowledgeSourceReference,
)
from azure.search.documents.knowledgebases import KnowledgeBaseRetrievalClient
from azure.search.documents.knowledgebases.models import (
    KnowledgeBaseMessage,
    KnowledgeBaseMessageTextContent,
    KnowledgeBaseRetrievalRequest,
    SearchIndexKnowledgeSourceParams,
)

# ---------------------------------------------------------------------------
# 1. Load environment
# ---------------------------------------------------------------------------
load_dotenv(override=True)

storage_connection_string = os.environ["AZURE_STORAGE_CONNECTION_STRING"]
search_endpoint = os.environ["SEARCH_SERVICE_ENDPOINT"]
search_key = os.environ["SEARCH_ADMIN_KEY"]
model_deployment_name = os.environ["MODEL_DEPLOYMENT_NAME"]
embedding_model_deployment_name = os.environ["EMBEDDING_MODEL_DEPLOYMENT_NAME"]
openai_endpoint = os.environ["AZURE_OPENAI_ENDPOINT"]
openai_key = os.environ["AZURE_OPENAI_KEY"]
project_resource_id = os.environ["AZURE_AI_PROJECT_RESOURCE_ID"]

knowledge_source_name = "machine-wiki-blob-ks"
knowledge_base_name = "machine-kb"
project_connection_name = "machine-wiki-connection"

index_client = SearchIndexClient(
    endpoint=search_endpoint, credential=AzureKeyCredential(search_key)
)

# ---------------------------------------------------------------------------
# 2. Create knowledge source (Blob Storage)
# ---------------------------------------------------------------------------
knowledge_source = AzureBlobKnowledgeSource(
    name=knowledge_source_name,
    description="This knowledge source pulls from a blob storage container.",
    encryption_key=None,
    azure_blob_parameters=AzureBlobKnowledgeSourceParameters(
        connection_string=storage_connection_string,
        container_name="machine-wiki",
        folder_path=None,
        is_adls_gen2=False,
        ingestion_parameters=KnowledgeSourceIngestionParameters(
            identity=None,
            disable_image_verbalization=False,
            chat_completion_model=KnowledgeBaseAzureOpenAIModel(
                azure_open_ai_parameters=AzureOpenAIVectorizerParameters(
                    resource_url=openai_endpoint,
                    deployment_name=model_deployment_name,
                    api_key=openai_key,
                    model_name=model_deployment_name,
                )
            ),
            embedding_model=KnowledgeSourceAzureOpenAIVectorizer(
                azure_open_ai_parameters=AzureOpenAIVectorizerParameters(
                    resource_url=openai_endpoint,
                    deployment_name=embedding_model_deployment_name,
                    api_key=openai_key,
                    model_name=embedding_model_deployment_name,
                )
            ),
            content_extraction_mode=KnowledgeSourceContentExtractionMode.MINIMAL,
            ingestion_schedule=None,
            ingestion_permission_options=None,
        ),
    ),
)

index_client.create_or_update_knowledge_source(knowledge_source)
print(f"✅ Knowledge source '{knowledge_source_name}' created or updated successfully.")

# ---------------------------------------------------------------------------
# 3. Create knowledge base
# ---------------------------------------------------------------------------
aoai_params = AzureOpenAIVectorizerParameters(
    resource_url=openai_endpoint,
    api_key=openai_key,
    deployment_name=model_deployment_name,
    model_name=model_deployment_name,
)

knowledge_base = KnowledgeBase(
    name=knowledge_base_name,
    description="This knowledge base handles questions about common issues with manufacturing machines",
    retrieval_instructions=(
        f"Use the {knowledge_source_name} to query potential root causes for problems by machine type"
    ),
    answer_instructions="Provide a single sentence for the likely cause of the issue based on the retrieved documents.",
    output_mode=KnowledgeRetrievalOutputMode.ANSWER_SYNTHESIS,
    knowledge_sources=[KnowledgeSourceReference(name=knowledge_source_name)],
    models=[KnowledgeBaseAzureOpenAIModel(azure_open_ai_parameters=aoai_params)],
    encryption_key=None,
    retrieval_reasoning_effort=KnowledgeRetrievalLowReasoningEffort,
)

index_client.create_or_update_knowledge_base(knowledge_base)
print(f"✅ Knowledge base '{knowledge_base_name}' created or updated successfully.")

# ---------------------------------------------------------------------------
# 4. Test the knowledge base
# ---------------------------------------------------------------------------
kb_client = KnowledgeBaseRetrievalClient(
    endpoint=search_endpoint,
    knowledge_base_name=knowledge_base_name,
    credential=AzureKeyCredential(search_key),
)

request = KnowledgeBaseRetrievalRequest(
    messages=[
        KnowledgeBaseMessage(
            role="assistant",
            content=[
                KnowledgeBaseMessageTextContent(
                    text="What can be the potential issue if curing_temperature is above 178°C"
                )
            ],
        ),
    ],
    knowledge_source_params=[
        SearchIndexKnowledgeSourceParams(
            knowledge_source_name=knowledge_source_name,
            include_references=True,
            include_reference_source_data=True,
            always_query_source=False,
        )
    ],
    include_activity=True,
)

result = kb_client.retrieve(request)
print("\n--- Knowledge base test result ---")
print(result.response[0].content[0].text)

# ---------------------------------------------------------------------------
# 5. Create project connection (exposes KB as MCP server)
# ---------------------------------------------------------------------------
credential = DefaultAzureCredential()
mcp_endpoint = (
    f"{search_endpoint}/knowledgebases/{knowledge_base_name}/mcp"
    "?api-version=2025-11-01-preview"
)

bearer_token_provider = get_bearer_token_provider(
    credential, "https://management.azure.com/.default"
)
headers = {"Authorization": f"Bearer {bearer_token_provider()}"}

response = requests.put(
    f"https://management.azure.com{project_resource_id}"
    f"/connections/{project_connection_name}?api-version=2025-10-01-preview",
    headers=headers,
    json={
        "name": project_connection_name,
        "type": "Microsoft.MachineLearningServices/workspaces/connections",
        "properties": {
            "authType": "ProjectManagedIdentity",
            "category": "RemoteTool",
            "target": mcp_endpoint,
            "isSharedToAll": True,
            "audience": "https://search.azure.com/",
            "metadata": {"ApiType": "Azure"},
        },
    },
    timeout=30,
)

response.raise_for_status()
print(f"\n✅ Connection '{project_connection_name}' created or updated successfully.")
