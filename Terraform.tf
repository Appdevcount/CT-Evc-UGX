parameters:
  - name: environment
    type: string
    values:
      - DV1
      - QA
      - IN1
      - PD1
  - name: servicePrincipal
    type: string
  - name: terraformStorageAccount
    type: string
  - name: resourceGroup
    type: string
  - name: terraformContainerName
    type: string
  - name: terraformStateFileName
    type: string
  - name: region
    type: string
    values:
      - eastus2
      - centralus
  - name: terraformWorkingDirectory
    type: string
  - name: assetOwner
    type: string
  - name: businessOwner
    type: string
  - name: appName
    type: string
  - name: itSponsor
    type: string

jobs:
  - deployment: Infrastructure
    timeoutInMinutes: 300
    environment: ${{ parameters.environment }}
    displayName: Deploy Infrastructure
    variables:
      env: ${{ lower(parameters.environment) }}
    strategy:
      runOnce:
        deploy:
          steps:
            - download: none
            - checkout: self
            - task: AzureCLI@2
              displayName: 'Create Terraform state storage'
              inputs:
                azureSubscription: ${{ parameters.servicePrincipal }}
                scriptType: 'bash'
                scriptLocation: 'inlineScript'
                inlineScript: |
                  az storage account create --name ${{ parameters.terraformStorageAccount }} --resource-group ${{ parameters.resourceGroup }} --location ${{ parameters.region }} --sku Standard_LRS --allow-blob-public-access false --min-tls-version TLS1_2 --tags 'CostCenter=61700200' 'AssetOwner=${{ parameters.assetOwner }}' 'BusinessOwner=${{ parameters.businessOwner }}' 'DataClassification=Sensitive' 'AppName=${{ parameters.appName }} ' 'ITSponsor=${{ parameters.itSponsor }}' 'Tier=3'
                  az storage container create --name ${{ parameters.terraformContainerName }} --account-name ${{ parameters.terraformStorageAccount }}
                  STORAGE_ACCOUNT_KEY=$(az storage account keys list -n ${{ parameters.terraformStorageAccount }} | jq ".[0].value" -r)
                  echo "setting storage account key variable"
                  echo "##vso[task.setvariable variable=ARM_ACCESS_KEY;issecret=true]$STORAGE_ACCOUNT_KEY"

            - task: ms-devlabs.custom-terraform-tasks.custom-terraform-installer-task.TerraformInstaller@0
              displayName: "Install Terraform"
              inputs:
                terraformVersion: "latest"

            - powershell: |
                git config --global --add url."https://eviCoreDev:$(System.AccessToken)@dev.azure.com/eviCoreDev/Terraform%20Modules/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules'
                git config --global --add url."https://eviCoreDev:$(System.AccessToken)@dev.azure.com/eviCoreDev/Terraform%20Modules/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%2520Modules'
                git config --global --add url."https://eviCoreDev:$(System.AccessToken)@dev.azure.com/eviCoreDev/eviCore%20Platform/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/eviCore%20Platform'
                git config --global --add url."https://eviCoreDev:$(System.AccessToken)@dev.azure.com/eviCoreDev/eviCore%20Platform/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/eviCore%2520Platform'
                git config --global -l
              displayName: Git url rewrite

            - task: TerraformTaskV2@2
              displayName: "Terraform init"
              inputs:
                provider: "azurerm"
                command: "init"
                workingDirectory: ${{ parameters.terraformWorkingDirectory }}
                backendServiceArm: ${{ parameters.servicePrincipal }}
                backendAzureRmResourceGroupName: ${{ parameters.resourceGroup }}
                backendAzureRmStorageAccountName: ${{ parameters.terraformStorageAccount }}
                backendAzureRmContainerName: ${{ parameters.terraformContainerName }}
                backendAzureRmKey: ${{ parameters.terraformStateFileName }}

            - task: TerraformTaskV2@2
              displayName: "Terraform plan"
              name: 'TerraformA'
              inputs:
                provider: "azurerm"
                command: "plan"
                workingDirectory: ${{ parameters.terraformWorkingDirectory }}
                commandOptions: '-var-file Environments/$(env).tfvars -var=resource_group_name="${{ parameters.resourceGroup }}" -out=tfplan.out'
                environmentServiceNameAzureRM: ${{ parameters.servicePrincipal }}

            - task: TerraformTaskV2@2
              displayName: "Terraform Apply"
              name: 'TerraformApply'
              inputs:
                provider: "azurerm"
                command: "apply"
                workingDirectory: ${{ parameters.terraformWorkingDirectory }}
                commandOptions: 'tfplan.out'
                environmentServiceNameAzureRM: ${{ parameters.servicePrincipal }}

            - task: PowerShell@2
              displayName: 'Read terraform outputs'
              name: terraformOutput
              inputs:
                targetType: 'inline'
                script: |
                  $terraformOutput = Get-Content "$(TerraformApply.jsonOutputVariablesPath)" | ConvertFrom-Json
                  $terraformOutput | Get-Member -MemberType NoteProperty | % { $o = $terraformOutput.($_.Name); Write-Host "##vso[task.setvariable variable=$($_.Name);isoutput=true]$($o.value)" }

  



Thanks and Regards
Siraj




Azure search
locals {
  location_abbreviations = {
    eastus         = "eu1"
    eastus2        = "eu2"
    westus         = "wu1"
    westus2        = "wu2"
    centralus      = "cus"
    southcentralus = "scu"
  }
  search_service_name     = var.account_name != "" ? var.account_name : "eh${local.location_abbreviations[var.location]}${var.environment}${var.domain}search"
  private_endpoint_name   = "eh${local.location_abbreviations[var.location]}${var.environment}-pe-${var.domain}"
  service_connection_name = "eh${local.location_abbreviations[var.location]}${var.environment}-psc-${var.domain}"
  #dns_zone_group_name     = "eh${local.location_abbreviations[var.location]}${var.environment}-zg-${var.domain}"
  tags = {
    CostCenter         = var.cost_center
    AssetOwner         = var.asset_owner
    BusinessOwner      = var.business_owner
    DataClassification = var.data_classification
    AppName            = var.app_name
    ITSponsor          = var.it_sponsor
    Tier               = var.tier
    AppTeam            = var.app_team
    AppOwner           = var.app_owner
    ServiceNowBA       = var.service_now_ba
    ServiceNowAS       = var.service_now_as
    SecurityReviewID   = var.security_review_id
  }
  merged_tags = merge(local.tags, var.tags)
}
resource "azurerm_search_service" "module" {
  name                          = local.search_service_name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = var.sku
  partition_count               = var.partitions
  replica_count                 = var.replicas
  public_network_access_enabled = var.public_network_access_enabled
  tags                          = local.merged_tags

  timeouts {
    create = var.timeout_create
    update = var.timeout_update
    delete = var.timeout_delete
  }

  identity {
    type = "SystemAssigned"
  }
}

resource "azurerm_private_endpoint" "search_endpoint" {
  count               = var.public_network_access_enabled ? 0 : 1
  location            = var.location
  name                = local.private_endpoint_name
  resource_group_name = var.resource_group_name
  subnet_id           = var.subnet_id
  tags                = local.merged_tags

  timeouts {
    create = var.timeout_create
    update = var.timeout_update
    delete = var.timeout_delete
  }

  private_service_connection {
    name                           = local.service_connection_name
    is_manual_connection           = false
    private_connection_resource_id = azurerm_search_service.module.id
    subresource_names              = ["searchService"]
  }
}

variable "environment" {
  type        = string
  default     = "dv1"
  description = "The name of the environment for the resource. Such as dv1, in1, pd1"
}

variable "location" {
  type        = string
  default     = "eastus2"
  description = "The location of the resource"
}

variable "domain" {
  type        = string
  description = "A domain or context for the resource. This value is appended to the resource name."
}

variable "resource_group_name" {
  type        = string
  description = "The resource group to add the resource."
}

variable "sku" {
  type        = string
  default     = "basic"
  description = "The SKU which should be used for the search service. Such as basic, free, standard"
}

variable "partitions" {
  type        = number
  default     = 1
  description = "The number of partitions in the search service"
}

output "id" {
  value = azurerm_search_service.module.id
}

output "name" {
  value = azurerm_search_service.module.name
}

output "primary_key" {
  value = azurerm_search_service.module.primary_key
}

output "secondary_key" {
  value = azurerm_search_service.module.secondary_key
}

output "msi_principal_id" {
  value = azurerm_search_service.module.identity[0].principal_id
}

output "msi_tenant_id" {
  value = azurerm_search_service.module.identity[0].tenant_id
}

Azure service plan
resource "azurerm_service_plan" "module" {
  name                         = local.name
  resource_group_name          = var.resource_group_name
  location                     = var.location
  os_type                      = var.os_type
  sku_name                     = var.sku_name
  worker_count                 = var.worker_count
  per_site_scaling_enabled     = var.per_site_scaling

  maximum_elastic_worker_count = var.maximum_elastic_worker_count == 0 ? null : var.maximum_elastic_worker_count

  timeouts {
    create = var.timeout_create
    update = var.timeout_update
    delete = var.timeout_delete
  }

  tags = {
    Tier               = local.tags.Tier
    CostCenter         = local.tags.CostCenter
    ITSponsor          = local.tags.ITSponsor
    AssetOwner         = local.tags.AssetOwner
    AppName            = local.tags.AppName
    BusinessOwner      = local.tags.BusinessOwner
    DataClassification = local.tags.DataClassification
    ServiceNowBA       = local.tags.ServiceNowBA
    ServiceNowAS       = local.tags.ServiceNowAS
    SecurityReviewID   = local.tags.SecurityReviewID
  }
}

AppInsights

locals {
  location_abbreviations = {
    eastus         = "eu1"
    eastus2        = "eu2"
    westus         = "wu1"
    westus2        = "wu2"
    centralus      = "cus"
    southcentralus = "scu"
  }
  name = "eh${local.location_abbreviations[var.location]}${var.environment}-ai-${var.domain}"
  tags = {
    CostCenter         = var.cost_center
    AssetOwner         = var.asset_owner
    BusinessOwner      = var.business_owner
    DataClassification = var.data_classification
    AppName            = var.app_name
    ITSponsor          = var.it_sponsor
    Tier               = var.tier
    AppTeam            = var.app_team
    AppOwner           = var.app_owner
    ServiceNowBA       = var.service_now_ba
    ServiceNowAS       = var.service_now_as
    SecurityReviewID   = var.security_review_id
  }
  merged_tags = var.override_tagging_props ? var.tags : merge(local.tags, var.tags)
}

resource "azurerm_application_insights" "module" {
  name                                = local.name
  location                            = var.location
  resource_group_name                 = var.resource_group_name
  application_type                    = var.application_type
  retention_in_days                   = var.retention_in_days
  workspace_id                        = azurerm_log_analytics_workspace.log_analytics.id
  tags                                = local.merged_tags
  internet_ingestion_enabled          = var.internet_ingestion_enabled
  internet_query_enabled              = var.internet_query_enabled
  force_customer_storage_for_profiler = var.force_customer_storage_for_profiler
  local_authentication_disabled       = var.local_authentication_disabled
  sampling_percentage                 = var.sampling_percentage
}

resource "azurerm_log_analytics_workspace" "log_analytics" {
  name                          = "eh${local.location_abbreviations[var.location]}${var.environment}-law-${var.domain}"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = var.law_sku
  retention_in_days             = var.retention_in_days
  tags                          = local.merged_tags
  internet_ingestion_enabled    = var.internet_ingestion_enabled
  internet_query_enabled        = var.internet_query_enabled
  local_authentication_disabled = var.local_authentication_disabled
}

resource "azurerm_log_analytics_linked_storage_account" "linked_storage" {
  count                 = length(var.storage_account_ids) > 0 ? 1 : 0
  data_source_type      = "Query"
  resource_group_name   = var.resource_group_name
  workspace_resource_id = azurerm_log_analytics_workspace.log_analytics.id
  storage_account_ids   = var.storage_account_ids
}


ep-cosmos-account-with-private-endpoint

locals {
  location_abbreviations = {
    eastus         = "eu1"
    eastus2        = "eu2"
    westus         = "wu1"
    westus2        = "wu2"
    centralus      = "cus"
    southcentralus = "scu"
  }
  name                    = var.account_name != "" ? var.account_name : "eh${local.location_abbreviations[var.location]}${var.environment}cdb${var.product}${var.domain}"
  capabilities            = var.serverless ? [{ name = "EnableServerless" }] : []
  default_ip_range_filter = "198.27.9.0/24,199.204.156.0/22"
  ip_range_portal         = var.enable_azure_portal_access ? "${local.default_ip_range_filter},104.42.195.92,40.76.54.131,52.176.6.30,52.169.50.45,52.187.184.26" : local.default_ip_range_filter
  ip_range_filter         = var.enable_azure_dc_access ? "${local.ip_range_portal},0.0.0.0" : local.ip_range_portal
  key_vault_key_id        = var.key_vault_key_id != null ? var.key_vault_key_id : null
  tags = {
    CostCenter              = var.cost_center
    AssetOwner              = var.asset_owner
    BusinessOwner           = var.business_owner
    DataClassification      = var.data_classification
    AppName                 = var.app_name
    ITSponsor               = var.it_sponsor
    Tier                    = var.tier
    AppTeam                 = var.app_team
    AppOwner                = var.app_owner
    ServiceNowBA            = var.service_now_ba
    ServiceNowAS            = var.service_now_as
    SecurityReviewID        = var.security_review_id 
    AppCategory             = var.app_category
    DataSubjectArea         = var.data_subject_area
    ComplianceDataCategory  = var.compliance_data_category
    BusinessEntity          = var.business_entity
    LineOfBusiness          = var.line_of_business  
  }
  merged_tags = merge(local.tags, var.tags)
}

resource "azurerm_cosmosdb_account" "module" {
  name                            = local.name
  location                        = var.location
  resource_group_name             = var.resource_group_name
  offer_type                      = var.offer_type
  kind                            = var.kind
  enable_multiple_write_locations = var.enable_multiple_write_locations

  public_network_access_enabled     = true
  ip_range_filter                   = local.ip_range_filter
  is_virtual_network_filter_enabled = length(var.vnet_subnets) > 0 ? true : false
  key_vault_key_id                  = local.key_vault_key_id

  consistency_policy {
    consistency_level       = var.consistency_level
    max_interval_in_seconds = var.max_interval_in_seconds
    max_staleness_prefix    = var.max_staleness_prefix
  }

  enable_automatic_failover = var.enable_automatic_failover

  geo_location {
    location          = var.location
    failover_priority = 0
  }

  dynamic "capabilities" {
    for_each = local.capabilities

    content {
      name = capabilities.value.name
    }
  }

  dynamic "virtual_network_rule" {
    for_each = var.vnet_subnets

    content {
      id = virtual_network_rule.value
    }
  }

  dynamic "geo_location" {
    for_each = var.failover_geo_locations

    content {
      location          = geo_location.value.location
      failover_priority = geo_location.value.failover_priority
    }
  }

  tags = local.merged_tags

  identity {
    type = "SystemAssigned"
  }

  access_key_metadata_writes_enabled= var.enable_access_key_metadata_writes
}

resource "azurerm_monitor_metric_alert" "cosmos_ru_warn" {
  name                = "${local.name}-ru-alert-warn"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_cosmosdb_account.module.id]
  dynamic "action" {
    for_each = var.monitor_action_group_id
    content {
        action_group_id = action.value
    }
  }

  tags = local.merged_tags
  window_size = "PT15M"

  criteria {
    metric_namespace = "Microsoft.DocumentDB/databaseAccounts"
    metric_name      = "NormalizedRUConsumption"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 70
  }

  severity = 2
}

resource "azurerm_monitor_metric_alert" "cosmos_ru_error" {
  name                = "${local.name}-ru-alert-error"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_cosmosdb_account.module.id]
 dynamic "action" {
    for_each = var.monitor_action_group_id
    content {
        action_group_id = action.value
    }
  }

  tags = local.merged_tags
  window_size = "PT15M"

  criteria {
    metric_namespace = "Microsoft.DocumentDB/databaseAccounts"
    metric_name      = "NormalizedRUConsumption"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 90
  }

  severity = 1
}

resource "azurerm_monitor_metric_alert" "cosmos_ru_critical" {
  name                = "${local.name}-ru-alert-critical"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_cosmosdb_account.module.id]
 dynamic "action" {
    for_each = var.monitor_action_group_id
    content {
        action_group_id = action.value
    }
  }

  tags = local.merged_tags
  window_size = "PT15M"

  criteria {
    metric_namespace = "Microsoft.DocumentDB/databaseAccounts"
    metric_name      = "NormalizedRUConsumption"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 99
  }

  severity = 0
}

resource "azurerm_monitor_metric_alert" "cosmos_request_latency" {
  name                = "${local.name}-request-latency-alert"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_cosmosdb_account.module.id]

  dynamic_criteria {
    metric_namespace  = "Microsoft.DocumentDB/DatabaseAccounts"
    metric_name       = "ServerSideLatency"
    aggregation       = "Average"
    operator          = "GreaterThan"
    alert_sensitivity = var.latency_alert_settings.alert_sensitivity
  }

 dynamic "action" {
    for_each = var.monitor_action_group_id
    content {
        action_group_id = action.value
    }
  }

  frequency   = var.latency_alert_settings.frequency
  window_size = var.latency_alert_settings.window_size
  severity    = var.latency_alert_settings.severity
  tags        = local.merged_tags
}

resource "azurerm_monitor_metric_alert" "cosmos_service_timeout" {
  name                = "${local.name}-alert-service-timeout"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_cosmosdb_account.module.id]
 
 dynamic "action" {
    for_each = var.monitor_action_group_id
    content {
        action_group_id = action.value
    }
  }
  tags = local.merged_tags
 
  window_size = var.service_timeout_setting.window_size
  frequency = var.service_timeout_setting.frequency

  criteria {
    metric_namespace = "Microsoft.DocumentDB/databaseAccounts"
    metric_name      = "TotalRequests"
    aggregation      = "Count"
    operator         = "GreaterThan"
    threshold        = var.service_timeout_setting.threshold
    dimension {
      name ="StatusCode"
      operator = "Include"
      values =["408"] # 408 Service time out
    }
  }
  description = "Alert triggerd when cosmos DB service status 408  i.e Service Time out"

  severity = var.service_timeout_setting.severity
}

resource "azurerm_monitor_metric_alert" "cosmos_service_availability" {
  name                = "${local.name}-alert-service-availability"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_cosmosdb_account.module.id]
 

dynamic "action" {
    for_each = var.monitor_action_group_id
    content {
        action_group_id = action.value
    }
  }
  tags = local.merged_tags

  window_size = var.service_availability_setting.window_size
  frequency = var.service_availability_setting.frequency
dynamic_criteria {
  metric_namespace = "Microsoft.DocumentDB/databaseAccounts"
    metric_name      = "ServiceAvailability"
    aggregation      = "Average"
    operator         = "LessThan"
    alert_sensitivity = "High"
}
  
  description = "Alert triggerd when cosmos DB service availability less than 100% on average "

  severity = var.service_availability_setting.severity
}



Key vault
locals {
  location_abbreviations = {
    eastus         = "eu1"
    eastus2        = "eu2"
    westus         = "wu1"
    westus2        = "wu2"
    centralus      = "cus"
    southcentralus = "scu"
  }
  name                    = var.name_override != "" ? var.name_override : "eh${local.location_abbreviations[var.location]}${var.environment}${var.product}${var.domain}akv"
  default_ip_range_filter = ["198.27.9.0/24", "199.204.156.0/22"]
  bypass                  = var.bypass_network_rules ? "AzureServices" : "None"
  tags = {
    CostCenter             = var.cost_center
    AssetOwner             = var.asset_owner
    ServiceNowBA           = var.service_now_ba
    ServiceNowAS           = var.service_now_as
    SecurityReviewID       = var.security_review_id
    AppTeam                = var.app_team
    AppName                = var.app_name
    AppCategory            = var.app_category
    AppOwner               = var.app_owner
    DataSubjectArea        = var.data_subject_area
    ComplianceDataCategory = var.compliance_data_category
    DataClassification     = var.data_classification
    BusinessEntity         = var.business_entity
    LineOfBusiness         = var.line_of_business
    BusinessOwner          = var.business_owner
    ITSponsor              = var.it_sponsor
    Tier                   = var.tier
  }
  merged_tags = var.override_tagging_props ? var.tags : merge(local.tags, var.tags)
}

resource "azurerm_key_vault" "module" {
  name                          = local.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  enabled_for_disk_encryption   = var.enabled_for_disk_encryption
  enable_rbac_authorization     = var.enable_rbac_authorization
  tenant_id                     = var.tenant_id
  purge_protection_enabled      = var.purge_protection_enabled
  sku_name                      = var.sku_name
  public_network_access_enabled = var.public_network_access_enabled
  network_acls {
    bypass                     = local.bypass
    default_action             = var.default_action
    ip_rules                   = local.default_ip_range_filter
    virtual_network_subnet_ids = var.vnet_subnets
  }

  tags = local.merged_tags
}

resource "azurerm_monitor_metric_alert" "kv_availability_warn" {
  name                = "${local.name}-availability-alert-warn"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_key_vault.module.id]

  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
      action_group_id = action.value
    }
  }

  tags = local.merged_tags

  criteria {
    metric_namespace = "Microsoft.KeyVault/vaults"
    metric_name      = "Availability"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = 90
  }

  severity = 2
}

resource "azurerm_monitor_metric_alert" "kv_availability_error" {
  name                = "${local.name}-availability-alert-error"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_key_vault.module.id]

  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
      action_group_id = action.value
    }
  }

  tags = local.merged_tags

  criteria {
    metric_namespace = "Microsoft.KeyVault/vaults"
    metric_name      = "Availability"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = 80
  }

  severity = 1
}

resource "azurerm_monitor_metric_alert" "kv_availability_critical" {
  name                = "${local.name}-availability-alert-critical"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_key_vault.module.id]

  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
      action_group_id = action.value
    }
  }

  tags = local.merged_tags

  criteria {
    metric_namespace = "Microsoft.KeyVault/vaults"
    metric_name      = "Availability"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = 50
  }

  severity = 0
}

resource "azurerm_monitor_metric_alert" "kv_request_latency" {
  name                = "${local.name}-request-latency-alert"
  resource_group_name = var.resource_group_name

  scopes = [azurerm_key_vault.module.id]

  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
      action_group_id = action.value
    }
  }

  frequency   = var.latency_alert_settings.frequency
  window_size = var.latency_alert_settings.window_size
  severity    = var.latency_alert_settings.severity
  tags        = local.merged_tags

  dynamic_criteria {
    metric_namespace    = "Microsoft.KeyVault/vaults"
    metric_name         = "ServiceApiLatency"
    aggregation         = "Average"
    operator            = "GreaterThan"
    alert_sensitivity   = var.latency_alert_settings.alert_sensitivity
  }
}



Thanks and Regards
Siraj

