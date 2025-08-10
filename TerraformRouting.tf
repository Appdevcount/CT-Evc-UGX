git config --global --add url."https://eviCoreDev:lum732w4ik3djk4dphwctwy6sunlbthcgpfkzf5b3trtxrv4rhda@dev.azure.com/eviCoreDev/Terraform%20Modules/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules'
git config --global --add url."https://eviCoreDev:lum732w4ik3djk4dphwctwy6sunlbthcgpfkzf5b3trtxrv4rhda@dev.azure.com/eviCoreDev/Terraform%20Modules/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%2520Modules'
git config --global --add url."https://eviCoreDev:lum732w4ik3djk4dphwctwy6sunlbthcgpfkzf5b3trtxrv4rhda@dev.azure.com/eviCoreDev/eviCore%20Platform/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/eviCore%20Platform'
git config --global --add url."https://eviCoreDev:lum732w4ik3djk4dphwctwy6sunlbthcgpfkzf5b3trtxrv4rhda@dev.azure.com/eviCoreDev/eviCore%20Platform/_git".insteadOf 'ssh://git@ssh.dev.azure.com/v3/eviCoreDev/eviCore%2520Platform'
git config --global -l


az login

az account set -s 999f8f5e-95e4-49a3-bf91-8ae58d4f0092

terraform init -backend-config=storage_account_name=testucxroutingtfstate -backend-config=container_name=terraform -backend-config=key=terraform.tfstate -backend-config=resource_group_name=test_rsg_eu2_requestrouting -backend-config=subscription_id=999f8f5e-95e4-49a3-bf91-8ae58d4f0092 -backend-config=tenant_id=595de295-db43-4c19-8b50-183dfd4a3d06 

terraform import -var-file env_vars/test.tfvars eu2-qa-api-vnet-integration /subscriptions/999f8f5e-95e4-49a3-bf91-8ae58d4f0092/resourceGroups/vnet-requestrouting-eu2-test/providers/Microsoft.Network/virtualNetworks/test-requestrouting-vnet/subnets/requestrouting-eu2-qa-api-vnet-integration-subnet 

terraform import -var-file env_vars/test.tfvars eu2-qa-application-gateway /subscriptions/999f8f5e-95e4-49a3-bf91-8ae58d4f0092/resourceGroups/vnet-requestrouting-eu2-test/providers/Microsoft.Network/virtualNetworks/test-requestrouting-vnet/subnets/requestrouting-eu2-qa-application-gateway-subnet

terraform import -var-file env_vars/test.tfvars eu2-qa-observer-vnet-integration /subscriptions/999f8f5e-95e4-49a3-bf91-8ae58d4f0092/resourceGroups/vnet-requestrouting-eu2-test/providers/Microsoft.Network/virtualNetworks/test-requestrouting-vnet/subnets/requestrouting-eu2-qa-observer-vnet-integration-subnet

terraform import -var-file env_vars/test.tfvars eu2-qa-private-endpoint /subscriptions/999f8f5e-95e4-49a3-bf91-8ae58d4f0092/resourceGroups/vnet-requestrouting-eu2-test/providers/Microsoft.Network/virtualNetworks/test-requestrouting-vnet/subnets/requestrouting-eu2-qa-private-endpoint-subnet




---------------------------------------------------------------------Request Search Below----------------------------------

az login

az account set -s c1f12bc8-af75-455c-9325-c0f5ca28f15d

terraform init -backend-config=storage_account_name=c1f12bc8af75455tfstate -backend-config=container_name=requestsearch-terraform-prod -backend-config=key=ucxrequestsearchservice.tfstate -backend-config=resource_group_name=deploymentSupport-rg -backend-config=subscription_id=c1f12bc8-af75-455c-9325-c0f5ca28f15d -backend-config=tenant_id=595de295-db43-4c19-8b50-183dfd4a3d06


terraform import -var-file Environments/prod.tfvars azurerm_api_management_api.ucxRequestSearchService /subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_apphub_apim/providers/Microsoft.ApiManagement/service/ehpd1apihosted/apis/ucxrequestsearchservice-api;rev=1 

terraform import -var-file Environments/prod.tfvars azurerm_api_management_product.apim_api_product /subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_apphub_apim/providers/Microsoft.ApiManagement/service/ehpd1apihosted/products/UCXAuthToken 

terraform import -var-file Environments/prod.tfvars azurerm_api_management_product_api.apim_api_product_api /subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_apphub_apim/providers/Microsoft.ApiManagement/service/ehpd1apihosted/products/UCXAuthToken/apis/ucxrequestsearchservice-api

terraform import -var-file Environments/prod.tfvars azurerm_api_management_api_policy.requestSearchService /subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_apphub_apim/providers/Microsoft.ApiManagement/service/ehpd1apihosted/apis/ucxrequestsearchservice-api

terraform import -var-file Environments/prod.tfvars azurerm_cosmosdb_account.account /subscriptions/c1f12bc8-af75-455c-9325-c0f5ca28f15d/resourceGroups/prod_eu2_ucx_requestsearch/providers/Microsoft.DocumentDB/databaseAccounts/eh-eu2-prod-cdbucxrequestsearch

terraform state rm "azurerm_api_management_api.ucxRequestSearchService"

Thanks and Regards
Siraj

From: R, Sirajudeen (CTR) 
Sent: Friday, August 16, 2024 5:15 PM
To: R, Sirajudeen (CTR) <sirajudeen.r@evicore.com>
Subject: trf-rtars

main.tf
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.88.0"
    } 
    azapi = {
      source  = "Azure/azapi"
      version = "~> 1.8.0"
    }
  }
}

provider "azurerm" {
  features {}
}
provider "azapi" {
}

provider "azurerm" {
  features {}
  alias           = "env_provider"
  subscription_id = var.subscription_id
}

provider "azurerm" {
  features {}
  alias                      = "key_vault_sub"
  subscription_id            = var.shared_kv_sub_id
  skip_provider_registration = true
}
provider "azapi" {
  alias                      = "key_vault_sub"
  subscription_id            = var.shared_kv_sub_id
  skip_provider_registration = true
}

provider "azurerm" {
  features {}
  alias                      = "cloudsiem_log_analytics"
  subscription_id            = var.cloudsiem_log_analytics_subscription_id
  skip_provider_registration = true
}

terraform {
  backend "azurerm" {
  }
}

data "azurerm_client_config" "current" {
}

data "azurerm_subnet" "private-endpoint-subnet" {
  depends_on           = [module.virtual_network.vnet]
  provider             = azurerm.env_provider
  name                 = var.private_endpoint_subnet_name
  resource_group_name  = var.vnet_resource_group
  virtual_network_name = module.virtual_network.vnet.name
}

data "azurerm_subnet" "api_vnet_integration_subnet" {
  depends_on           = [module.virtual_network.vnet]
  provider             = azurerm.env_provider
  name                 = var.api_vnet_integration_subnet_name
  resource_group_name  = var.vnet_resource_group
  virtual_network_name = module.virtual_network.vnet.name
}

data "azurerm_subnet" "observer_vnet_integration_subnet" {
  depends_on           = [module.virtual_network.vnet]
  provider             = azurerm.env_provider
  name                 = var.observer_vnet_integration_subnet_name
  resource_group_name  = var.vnet_resource_group
  virtual_network_name = module.virtual_network.vnet.name
}

data "azurerm_subnet" "observer_vnet_integration_subnet_larger" {
  depends_on           = [module.virtual_network.vnet]
  provider             = azurerm.env_provider
  name                 = var.observer_vnet_integration_larger_subnet_name
  resource_group_name  = var.vnet_resource_group
  virtual_network_name = module.virtual_network.vnet.name
}

data "azurerm_subnet" "appgateway_subnet" {
  depends_on           = [module.virtual_network.vnet]
  provider             = azurerm.env_provider
  name                 = var.appgateway_subnet_name
  resource_group_name  = var.vnet_resource_group
  virtual_network_name = module.virtual_network.vnet.name
}

data "azurerm_log_analytics_workspace" "requestrouting_law" {
  depends_on          = [module.app_insights]
  name                = "eh${local.location_abbreviations[var.location]}${var.environment}-law-${var.name}"
  resource_group_name = var.resource_group
}

data "azurerm_log_analytics_workspace" "cloudsiem_log_analytics_workspace" {
  provider            = azurerm.cloudsiem_log_analytics
  name                = var.cloudsiem_log_analytics_name
  resource_group_name = var.cloudsiem_log_analytics_resource_group
}

data "azurerm_linux_web_app" "observer_blue" {
  name = "${var.environment}-requestrouting-observer-blue"
  resource_group_name =  var.resource_group
}

data "azurerm_linux_web_app" "observer_green" {
  name = "${var.environment}-requestrouting-observer-green"
  resource_group_name =  var.resource_group
}


Local.tf
locals {
  product           = "ucx"

  gravity_users = [
    "477f54da-ee31-4a6c-8888-6c43d7cf29d9", #Ben
    "581b3c0b-1620-4c39-b740-284f7484dfe0", #David
    "13a31833-b439-45e3-ac9c-49307c115096"  #Usman
  ]
  thunder_users = [
    "73cc5c4e-8352-4650-92cb-e11d58fdc9f7", #Chad
    "9c3a7000-6b85-437a-ae81-93c8fcfb43db", #Stephen
    "52fe4f4c-a012-4cde-9f75-2d979ace4197", #Ron
    "0e7f1419-8aeb-4d8d-b5eb-59938897b1dd", #Bijay
    "6235d95d-bcc4-4129-b1eb-a3751769af6b", #Anil
  ]

  all_users = toset(concat(local.gravity_users, local.thunder_users))

  data_at_rest_tags = {
    DataSubjectArea         ="clinical"
    ComplianceDataCategory  =var.tag_ComplianceDataCategory
    DataClassification      =var.tag_DataClassification
    BusinessEntity          ="evicore"
    LineOfBusiness          ="healthServices"
  }

  merged_tags = merge(var.tags, local.data_at_rest_tags)

 redis_data_at_rest_tags ={
    ComplianceDataCategory = "none"
    DataClassification  = "internal"
  }

  tags = var.tags
  tag_AppTeam = "Thunder"

  location_abbreviations = {
    eastus         = "eu1"
    eastus2        = "eu2"
    westus         = "wu1"
    westus2        = "wu2"
    centralus      = "cus"
    southcentralus = "scu"
  }

  app_gateway_frontend_port = {
    "appGatewayFrontendPortHTTP" = {
      FRONTEND_PORT_NUMBER = 80
    }
    "appGatewayFrontendPortHTTPS" = {
      FRONTEND_PORT_NUMBER = 443
    }
  }

  app_gateway_backend_address_pool = {
    "RequestRoutingBackendPool" = {
      BACKEND_ADDRESS_POOL_FQDNS = [azurerm_linux_web_app.request_routing_api.default_hostname]
      BACKEND_ADDRESS_POOL_NAME  = "RequestRoutingBackendPool"
    }
  }

  shared_kv_secret = {
    "secret-wildcard-evicore-pfx-pwd-pvt"    = {}
    "secret-wildcard-evicore-pfx-base64-pvt" = {}
  }

  app_gateway_ssl_certificate = {
    "secret-wildcard-evicore-pfx-base64-pvt" = {
      AKV_CERTIFICATE_NAME   = "eviCoreWildCardCertificate"
      AKV_CERTIFICATE_SECRET = "secret-wildcard-evicore-pfx-pwd-pvt"
    }
  }

  app_gateway_backend_http_settings = {
    "RequestRoutingHTTPS" = {
      BACKEND_HTTP_SETTINGS_NAME                           = "RequestRoutingHTTPS"
      BACKEND_HTTP_SETTINGS_PORT                           = 443
      BACKEND_HTTP_SETTINGS_PROTOCOL                       = "Https"
      BACKEND_HTTP_SETTINGS_COOKIE_AFFINITY                = "Disabled"
      BACKEND_HTTP_SETTINGS_HOSTNAME_FROM_BACKEND          = true
      BACKEND_HTTP_SETTINGS_HOSTNAME                       = ""
      BACKEND_HTTP_SETTINGS_TIMEOUT                        = 120
      BACKEND_HTTP_SETTINGS_PROBE_NAME                     = "RequestRouting_Probe"
      BACKEND_HTTP_SETTINGS_TRUSTED_ROOT_CERTIFICATE_NAMES = []
    }
  }

  app_gateway_http_listener = {
    "RequestRoutingListenerHTTPS" = {
      HTTP_LISTENER_NAME                 = "RequestRoutingListenerHTTPS"
      HTTP_LISTENER_PROTOCOL             = "Https"
      HTTP_LISTENER_FRONTEND_PORT_NAME   = "appGatewayFrontendPortHTTPS"
      HTTP_LISTENER_FRONTEND_IP_CFG_NAME = "appGatewayPrivateFrontendIP"
      HTTP_LISTENER_HOSTNAME             = "${var.environment}-${var.product}-${var.name}-api.evicore.com"
      HTTP_LISTENER_REQUIRE_SNI          = true
      HTTP_LISTENER_CERTIFICATE          = "eviCoreWildCardCertificate"
      HTTP_LISTENER_FIREWALL_POLICY_ID   = azurerm_web_application_firewall_policy.waf_policy.id
    }
    "RequestRoutingListenerHTTP" = {
      HTTP_LISTENER_NAME                 = "RequestRoutingListenerHTTP"
      HTTP_LISTENER_PROTOCOL             = "Http"
      HTTP_LISTENER_FRONTEND_PORT_NAME   = "appGatewayFrontendPortHTTP"
      HTTP_LISTENER_FRONTEND_IP_CFG_NAME = "appGatewayPrivateFrontendIP"
      HTTP_LISTENER_HOSTNAME             = "${var.environment}-${var.product}-${var.name}-api.evicore.com"
      HTTP_LISTENER_REQUIRE_SNI          = false
      HTTP_LISTENER_CERTIFICATE          = ""
      HTTP_LISTENER_FIREWALL_POLICY_ID   = azurerm_web_application_firewall_policy.waf_policy.id
    }
  }

  app_gateway_routing_rule = {
    "RequestRoutingHTTPSRedirectRule" = {
      ROUTING_RULE_NAME          = "RequestRoutingHTTPSRedirectRule"
      ROUTING_RULE_TYPE          = "Basic"
      HTTP_LISTENER_NAME         = "RequestRoutingListenerHTTP"
      BACKEND_ADDRESS_POOL_NAME  = "RequestRoutingBackendPool"
      BACKEND_HTTP_SETTINGS_NAME = "RequestRoutingHTTPS"
      REDIRECT_CONFIG_NAME       = "RequestRoutingHTTPSRedirectConfig"
      URL_PATH_MAP_NAME          = ""
      REWRITE_RULE_SET_NAME      = ""
      PRIORITY                   = 150
    }
    "RequestRoutingRuleHTTPS" = {
      ROUTING_RULE_NAME          = "RequestRoutingRuleHTTPS"
      ROUTING_RULE_TYPE          = "Basic"
      HTTP_LISTENER_NAME         = "RequestRoutingListenerHTTPS"
      BACKEND_ADDRESS_POOL_NAME  = "RequestRoutingBackendPool"
      BACKEND_HTTP_SETTINGS_NAME = "RequestRoutingHTTPS"
      REDIRECT_CONFIG_NAME       = ""
      URL_PATH_MAP_NAME          = ""
      REWRITE_RULE_SET_NAME      = null
      PRIORITY                   = 160
    }
  }

  app_gateway_probe = {
    "RequestRouting_Probe" = {
      PROBE_PROTOCOL                   = "Https"
      PROBE_PATH                       = "/api/health"
      PROBE_INTERVAL                   = 30
      PROBE_TIMEOUT                    = 120
      PROBE_UNHEALTHY_THRESHOLD        = 3
      PROBE_PICK_HOSTNAME_FROM_BACKEND = true
      PROBE_MINIMUM_SERVERS            = 0
      PROBE_MATCH_BODY                 = ""
      PROBE_MATCH_STATUS_CODE          = ["200-404"]
    }
  }

  app_gateway_redirect_configuration = {
    "RequestRoutingHTTPSRedirectConfig" = {
      REDIRECT_CONFIGURATION_REDIRECT_TYPE        = "Permanent"
      REDIRECT_CONFIGURATION_INCLUDE_PATH         = true
      REDIRECT_CONFIGURATION_INCLUDE_QUERY_STRING = true
      REDIRECT_CONFIGURATION_TARGET_LISTENER_NAME = "RequestRoutingListenerHTTPS"
    }
  }

  app_gateway_redirect_configuration_target = {
  }

  monitor_app_gateway_diagnostic_log_category = {
    "ApplicationGatewayAccessLog" = {
      DIAGNOSTIC_LOG_ENABLED = true
    }
    "ApplicationGatewayPerformanceLog" = {
      DIAGNOSTIC_LOG_ENABLED = true
    }
    "ApplicationGatewayFirewallLog" = {
      DIAGNOSTIC_LOG_ENABLED = true
    }
  }

  monitor_app_gateway_diagnostic_metric_category = {
    "AllMetrics" = {
      DIAGNOSTIC_METRIC_ENABLED = true
    }
  }

  app_gateway_waf_configurations = {
    configuration_1 = {
      gateway_waf_enabled          = true
      gateway_waf_firewall_mode    = "Detection"
      gateway_waf_rule_set_type    = "OWASP"
      gateway_waf_rule_set_version = "3.2"
    }
  }

  scoring_profiles = [
    {
      scoringProfileName = "SleepMdSp"
      weights = {
        Priority             = 6000
        JurisdictionState    = 20
        Status               = 2
        SslRequirementStatus = 1
        AssignedToId         = 10
      }
      freshnessFunctions = [
        {
          fieldName         = "DueDateUtc"
          boostValue        = 5
          boostingDuration  = "0:2:0:0"
          isFutureDated     = "true"
          Interpolation     = "Linear"
        },
        {
          fieldName         = "DueDateUtc"
          boostValue        = 10
          boostingDuration  = "10:0:0"
          isFutureDated     = "false"
          Interpolation     = "Linear"
        }
      ]
    }
  ]
  
  user_groups = [
    {
      Name                 = "EPSleepMedicalDirector",
      UserType             = "MD"
    },
     {
      Name                 = "EpPacMedicalDirector",
      UserType             = "MD"
    },
    {
      Name                 = "ucx_md",
      UserType             = "MD"
    },
    {
      Name                 = "EpSleepNurse",
      UserType             = "Nurse"
    },
    {
      Name                 = "EpPacNurse",
      UserType             = "Nurse"
    },
    {
      Name                 = "EpPacClinicalNurseSupervisor",
      UserType             = "Nurse"
    },
    {
      Name                 = "EpPacNurseCCR",
      UserType             = "Nurse"
    }
  ]
}

Vnet.tf
module "virtual_network" {
  source                         = "./modules/vnet"
  application                    = var.name
  rg_name                        = var.vnet_resource_group
  location                       = var.location
  vnetcidr                       = var.vnet_cidr
  subnets                        = var.subnets
  tags                           = var.tags
  production                     = var.production
  enable_flow_logs               = var.enable_flow_logs
  flow_log_storage_account_id    = data.azurerm_storage_account.tfstatestorage.id
  monitor_log_storage_account_id = data.azurerm_storage_account.tfstatestorage.id
  environment                    = var.environment
}
Main.tf
terraform {
  required_version = ">= 1.2.2"

  required_providers {
    azurerm = "~> 3.88.0"
  }
}

data "azurerm_subscription" "current_subscription" {}

data "azurerm_resource_group" "target_rg" {
  name = var.rg_name
}

resource "azurerm_network_security_group" "default" {
  name                = format("%s%s", var.application, "nsg")
  location            = var.location
  resource_group_name = data.azurerm_resource_group.target_rg.name

  tags = merge(
    var.tags,
    var.user_defined_tags,
  )
}

resource "azurerm_network_watcher" "default" {
  name                = "NetworkWatcher_${lower(azurerm_virtual_network.goldenVNET.location)}"
  location            = azurerm_virtual_network.goldenVNET.location
  resource_group_name = "NetworkWatcherRG"
  tags                = var.tags
}

resource "azurerm_virtual_network" "goldenVNET" {
  name                = var.environment != "" ? format("%s%s%s%s", var.environment, "-", var.application, "-vnet") : format("%s%s", var.application, "-vnet")
  resource_group_name = data.azurerm_resource_group.target_rg.name
  location            = var.location
  address_space       = var.vnetcidr
  dns_servers         = var.production == false ? (var.location == "centralus" ? var.dns_servers["nonprod-cus"] : var.dns_servers["nonprod-eu2"]) : (var.location == "centralus" ? var.dns_servers["prod-cus"] : var.dns_servers["prod-eu2"])

  tags = merge(
    var.tags,
    var.user_defined_tags,
  )
}

locals {
  protected_subnet_names = [
    "AzureBastionSubnet",
    "AzureFirewallSubnet",
    "GatewaySubnet"
  ]

  subnet_names = {
  for subnet_name, subnet_cidr in var.subnets :
  subnet_name => contains(local.protected_subnet_names, subnet_name) ? subnet_name : format("%s%s%s%s", var.application, "-", subnet_name, "-subnet")
  }
}

resource "azurerm_subnet" "goldenVNET_sub" {
  for_each                                  = var.subnets
  name                                      = local.subnet_names[each.key]
  virtual_network_name                      = azurerm_virtual_network.goldenVNET.name
  resource_group_name                       = data.azurerm_resource_group.target_rg.name
  address_prefixes                          = each.value.address_prefixes
  service_endpoints                         = each.value.service_endpoints
  private_endpoint_network_policies_enabled = each.value.private_endpoint_network_policies_enabled

  dynamic "delegation" {
    for_each = each.value.delegation_name != null ? [1] : []
    content {
      name = each.value.delegation_name
      service_delegation {
        name    = each.value.service_delegation_name
        actions = each.value.service_delegation_actions
      }
    }
  }
}

# The var.subnets.managednsg value set to 'true' will prevent default NSG association
resource "azurerm_subnet_network_security_group_association" "nsg_assoc" {
  for_each = {
  for k, v in var.subnets : k => v
  if v.managednsg == false
  }

  subnet_id                 = azurerm_subnet.goldenVNET_sub[each.key].id
  network_security_group_id = azurerm_network_security_group.default.id
}


resource "azurerm_route_table" "resource" {
  name                          = format("%s%s%s%s", var.location, "-", var.application, "-default-internet-rt")
  location                      = data.azurerm_resource_group.target_rg.location
  resource_group_name           = data.azurerm_resource_group.target_rg.name
  disable_bgp_route_propagation = false

  route {
    name                   = "FirewallDefaultRoute"
    address_prefix         = "0.0.0.0/0"
    next_hop_type          = "VirtualAppliance"
    next_hop_in_ip_address = var.production == false ? var.firewall_ip_address["nonprod-eu2"] : (var.location == "centralus" ? var.firewall_ip_address["prod-cus"] : var.firewall_ip_address["prod-eu2"])
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [
      tags ["createdBy"],
      tags ["CreatedDate"],
      tags ["createdDateTime"],
      tags ["Environment"],
    ]
  }
}

# The var.subnets.managedroutetable value set to 'true' will prevent this route table association
resource "azurerm_subnet_route_table_association" "resource" {
  for_each = {
  for k, v in var.subnets : k => v
  if v.managedroutetable == false
  }

  subnet_id      = azurerm_subnet.goldenVNET_sub[each.key].id
  route_table_id = azurerm_route_table.resource.id
}

################################################################################
# VNet Variables
################################################################################

variable "application" {
  description = "The prefix used for all resources in this example"
  type        = string
}

variable "rg_name" {
  type        = string
  description = "The resource group to which all the network resources should be added"
}

variable "location" {
  description = "Region to deploy to"
  type        = string
  default     = "eastus2"
}

variable "environment" {
  description = "(Optional if needed to be used in VNET name) The name of the environment for the resource, such as dev, test, prod."
  type        = string
  default     = ""
}

variable "vnetcidr" {
  description = "The CIDR of the routable VNET"
  type        = list
}

variable "dns_servers" {
  description = "Custom DNS server combinations for each environment"
  type        = map
  default     = {
    "nonprod-eu2" = ["10.193.0.54", "10.193.24.52"]
    "nonprod-cus" = ["10.193.24.52", "10.193.0.54"]
    "prod-cus"    = ["10.194.32.36", "10.194.46.52"]
    "prod-eu2"    = ["10.194.46.52", "10.194.32.36"]
  }
}

# Subnets used with some services require their own network security group (NSG).
# Set the 'managednsg' value to 'true' to prevent assignment of the default NSG.

variable "subnets" {
  description = "Map of subnets"
  type        = map(object({
    address_prefixes                          = list(string)
    private_endpoint_network_policies_enabled = bool
    service_endpoints                         = optional(list(string))
    managednsg                                = bool
    managedroutetable                         = bool
    delegation_name                           = optional(string)
    service_delegation_name                   = optional(string)
    service_delegation_actions                = optional(list(string))
  }))
}
/* example

  subnets                                               = {
    "eu2_dv1_snet_productname_private_endpoint"         = {
      address_prefixes                                  = ["10.193.100.0/28"]
      private_endpoint_network_policies_enabled         = false
      service_endpoints                                 = []
      managednsg                                        = false
      managedroutetable                                 = false
    }
    "eu2_dv1_snet_productname"                          = {
      address_prefixes                                  = ["10.193.100.16/28"]
      private_endpoint_network_policies_enabled         = true
      service_endpoints                                 = ["Microsoft.Storage",]
      managednsg                                        = false
      managedroutetable                                 = false
    }
    "eu2_dv1_snet_productname_vnet_integration"         = {
      address_prefixes                                  = ["10.193.100.32/28"]
      private_endpoint_network_policies_enabled         = true
      service_endpoints                                 = ["Microsoft.AzureCosmosDB",]
      managednsg                                        = false
      managedroutetable                                 = false
      delegation_name                                   = "appServiceDelegation"
      service_delegation_name                           = "Microsoft.Web/serverFarms"
      service_delegation_actions                        = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }

*/

variable "production" {
  description = "Production or non-production"
  type        = bool
  default     = false
}

variable "tags" {
  description = "Required tags (reference https://evicorehealthcare.atlassian.net/wiki/spaces/~131354132/pages/591593557/Azure+Tagging+Strategy)."
  type        = object({
    AppName            = string
    AppCategory        = string
    AssetOwner         = string
    BusinessOwner      = string
    CostCenter         = string
    DataClassification = string
    ITSponsor          = string
    ServiceNowBA       = string
    ServiceNowAS       = string
    SecurityReviewID   = string
    Tier               = string
  })
  validation {
    condition     = var.tags.CostCenter != "" && var.tags.AssetOwner != "" && var.tags.BusinessOwner != "" && var.tags.DataClassification != "" && var.tags.AppName != "" && var.tags.ITSponsor != "" && var.tags.Tier != ""
    error_message = "Defining all tags is required for this resource (reference https://evicorehealthcare.atlassian.net/wiki/spaces/~131354132/pages/591593557/Azure+Tagging+Strategy)."
  }
}

variable "user_defined_tags" {
  description = "Optional user tags (reference https://evicorehealthcare.atlassian.net/wiki/spaces/~131354132/pages/591593557/Azure+Tagging+Strategy)"
  type        = map(string)
  default     = {}
}

variable "monitor_log_storage_account_id" {
  type        = string
  default     = null
  description = "The id for the storage account to which monitor logs should be sent."
}

variable "enable_flow_logs" {
  type    = bool
  default = false
}

variable "flow_log_storage_account_id" {
  type        = string
  default     = null
  description = "The id for the storage account to which flow logs should be sent."
}

variable "firewall_ip_address" {
  description = "Azure firewall routing IP addresses"
  type        = map(string)
  default     = {
    "nonprod-eu2" = "10.193.1.196"
    "prod-cus"    = "10.194.33.4"
    "prod-eu2"    = "10.194.46.132"
  }
}

database.tf
module "cosmosdb-account" {
  source                     = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-cosmos-account?ref=v1.9.0"
  environment                = var.environment
  location                   = var.location
  domain                     = var.name
  product                    = "ucx"
  consistency_level          = "BoundedStaleness"
  max_interval_in_seconds    = 10
  max_staleness_prefix       = 100
  resource_group_name        = var.resource_group
  monitor_action_group_id    = azurerm_monitor_action_group.requestRouting_thunder_email_alerts.id
  enable_azure_portal_access = true
  enable_azure_dc_access     = true
  vnet_subnets               = [
    data.azurerm_subnet.observer_vnet_integration_subnet.id,
    data.azurerm_subnet.api_vnet_integration_subnet.id,
    data.azurerm_subnet.observer_vnet_integration_subnet_larger.id
  ]
  account_name = "eheu2${var.environment}cdbucx${var.name}"
  tags         = local.merged_tags
  cost_center         = var.cost_center
  asset_owner         = var.asset_owner
  business_owner      = var.business_owner
  data_classification = var.data_classification
  app_name            = var.name
  it_sponsor          = var.it_sponsor
  tier                = 1
  zone_redundant      = true
  }

module "cosmosdb-sql-database" {
  source                      = "git@ssh.dev.azure.com:v3/eviCoreDev/eviCore%20Platform/ep-terraform-modules//cosmosdb-sql-database/resource?ref=v3.9.2"
  name                        = "${var.environment}${var.name}"
  account_name                = module.cosmosdb-account.name
  account_resource_group_name = var.resource_group
  auto_scale_max_throughput   = 4000
}

resource "azurerm_cosmosdb_sql_container" "user-configuration" {
  name                = var.user_configuration_container_name
  resource_group_name = var.resource_group
  account_name        = module.cosmosdb-account.name
  database_name       = module.cosmosdb-sql-database.name
  partition_key_path  = "/id"

  autoscale_settings {
    max_throughput = var.cosmos_max_throughput
  }

  unique_key {
    paths = []
  }

  lifecycle {
     prevent_destroy = true
    ignore_changes = [
      unique_key
    ]
  }
}

resource "azurerm_cosmosdb_sql_container" "request-history-container-blue" {
  name                = var.history_container_name_blue
  resource_group_name = var.resource_group
  account_name        = module.cosmosdb-account.name
  database_name       = module.cosmosdb-sql-database.name
  partition_key_path  = var.cosmos_partition_key

  autoscale_settings {
    max_throughput = var.cosmos_history_container_max_throughput
  }

  unique_key {
    paths = []
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      unique_key
    ]
  }
}

resource "azurerm_cosmosdb_sql_container" "request-history-container-green" {
  name                = var.history_container_name_green
  resource_group_name = var.resource_group
  account_name        = module.cosmosdb-account.name
  database_name       = module.cosmosdb-sql-database.name
  partition_key_path  = var.cosmos_partition_key
  autoscale_settings {
    max_throughput = var.cosmos_history_container_max_throughput
  }
  unique_key {
    paths = []
  }
  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      unique_key
    ]
  }
}

resource "azurerm_cosmosdb_sql_container" "state-configuration-container" {
  name                = var.state_configuration_container_name
  resource_group_name = var.resource_group
  account_name        = module.cosmosdb-account.name
  database_name       = module.cosmosdb-sql-database.name
  partition_key_path  = var.state_configuration_partition_key

  autoscale_settings {
    max_throughput = var.cosmos_max_throughput
  }

  unique_key {
    paths = []
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      unique_key
    ]
  }
}

resource "azurerm_cosmosdb_sql_container" "user-profile-container" {
  name                = var.user_profile_container_name
  resource_group_name = var.resource_group
  account_name        = module.cosmosdb-account.name
  database_name       = module.cosmosdb-sql-database.name
  partition_key_path  = var.user_profile_partition_key

  autoscale_settings {
    max_throughput = var.cosmos_max_throughput
  }

  unique_key {
    paths = []
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes = [
      unique_key
    ]
  }
}

resource "azurerm_private_endpoint" "cosmos_private_endpoint" {
  location                = var.location
  name                    = "eh${local.location_abbreviations[var.location]}-pe-${var.name}-cosmosdb"
  resource_group_name     = var.resource_group
  subnet_id               = data.azurerm_subnet.private-endpoint-subnet.id
  tags                    = var.tags

  private_service_connection {
    name                            = "eh${local.location_abbreviations[var.location]}${var.environment}-psc-${var.name}-cosmosdb"
    is_manual_connection            = false
    private_connection_resource_id  = module.cosmosdb-account.id
    subresource_names               = ["SQL"]
  }

  private_dns_zone_group {
    name                 = "eh${local.location_abbreviations[var.location]}${var.environment}-zg-${var.name}"
    private_dns_zone_ids = [
      "/subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_private_dns/providers/Microsoft.Network/privateDnsZones/privatelink.documents.azure.com"
    ]
  }
}

cognitive-search.tf
resource "azurerm_search_service" "request-routing-search" {
  name                          = "eheu2-${var.environment}-${var.name}-acs"
  location                      = var.location
  resource_group_name           = var.resource_group
  sku                           = var.search_sku
  partition_count               = 1
  replica_count                 = var.search_replicas
  public_network_access_enabled = false
  tags                          = local.merged_tags
  allowed_ips                   = var.vpn_ips_cidr
}

resource "azurerm_private_endpoint" "cognitive-search-endpoint" {
  location            = var.location
  name                = "eheu2${var.environment}-pe-${var.name}"
  resource_group_name = var.resource_group
  subnet_id           = data.azurerm_subnet.private-endpoint-subnet.id
  tags                = var.tags

  private_service_connection {
    name                           = "eheu2${var.environment}-psc-${var.name}"
    is_manual_connection           = false
    private_connection_resource_id = azurerm_search_service.request-routing-search.id
    subresource_names              = ["searchService"]
  }

  private_dns_zone_group {
    name                 = "eheu2${var.environment}-zg-${var.name}"
    private_dns_zone_ids = [
      "/subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_private_dns/providers/Microsoft.Network/privateDnsZones/privatelink.search.windows.net"
    ]
  }
}

key-vault.tf
data "azurerm_key_vault" "key_vault" {
  name                = "eh${local.location_abbreviations[var.location]}${var.environment}${var.product}${var.short_name}akv"
  resource_group_name = var.resource_group
}

resource "azurerm_private_endpoint" "keyvault_private_endpoint" {
  location                = var.location
  name                    = "eh${local.location_abbreviations[var.location]}-pe-${var.name}-keyvault"
  resource_group_name     = var.resource_group
  subnet_id               = data.azurerm_subnet.private-endpoint-subnet.id
  tags                    = var.tags

  private_service_connection {
    name                            = "eh${local.location_abbreviations[var.location]}${var.environment}-psc-${var.name}-keyvault"
    is_manual_connection            = false
    private_connection_resource_id  = data.azurerm_key_vault.key_vault.id
    subresource_names               = ["vault"]
  }

  private_dns_zone_group {
    name                 = "eh${local.location_abbreviations[var.location]}${var.environment}-zg-${var.name}"
    private_dns_zone_ids = [
      "/subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_private_dns/providers/Microsoft.Network/privateDnsZones/privatelink.vaultcore.azure.net"
    ]
  }
}

resource "azurerm_monitor_metric_alert" "kv_availability_warn" {
  name                = "${data.azurerm_key_vault.key_vault.name}-availability-alert-warn"
  resource_group_name = var.resource_group

  scopes = [data.azurerm_key_vault.key_vault.id]
  action {
    action_group_id = azurerm_monitor_action_group.requestRouting_thunder_email_alerts.id
  }

  tags = var.tags

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
  name                = "${data.azurerm_key_vault.key_vault.name}-availability-alert-error"
  resource_group_name = var.resource_group

  scopes = [data.azurerm_key_vault.key_vault.id]
  action {
    action_group_id = azurerm_monitor_action_group.requestRouting_thunder_email_alerts.id
  }

  tags = var.tags

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
  name                = "${data.azurerm_key_vault.key_vault.name}-availability-alert-critical"
  resource_group_name = var.resource_group

  scopes = [data.azurerm_key_vault.key_vault.id]
  action {
    action_group_id = azurerm_monitor_action_group.requestRouting_thunder_email_alerts.id
  }

  tags = var.tags

  criteria {
    metric_namespace = "Microsoft.KeyVault/vaults"
    metric_name      = "Availability"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = 50
  }

  severity = 0
}

module "observer_blue_access_policy_v1" {
  source             = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-key-vault-access-policy?ref=v1.0.0"
  key_vault_id       = data.azurerm_key_vault.key_vault.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = data.azurerm_linux_web_app.observer_blue.identity.0.principal_id
  secret_permissions = ["Get", "List"]
  key_permissions    = ["Get", "List"]
}

module "observer_green_access_policy_v1" {
  source             = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-key-vault-access-policy?ref=v1.0.0"
  key_vault_id       = data.azurerm_key_vault.key_vault.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = data.azurerm_linux_web_app.observer_green.identity.0.principal_id
  secret_permissions = ["Get", "List"]
  key_permissions    = ["Get", "List"]
}

module "api_access_policy_v1" {
  source             = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-key-vault-access-policy?ref=v1.0.0"
  key_vault_id       = data.azurerm_key_vault.key_vault.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = azurerm_linux_web_app.request_routing_api.identity.0.principal_id
  secret_permissions = ["Get", "List"]
  key_permissions    = ["Get", "List"]
}

module "api_staging_access_policy_v1" {
  source             = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-key-vault-access-policy?ref=v1.0.0"
  key_vault_id       = data.azurerm_key_vault.key_vault.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  object_id          = azurerm_linux_web_app_slot.request_routing_api_staging.identity.0.principal_id
  secret_permissions = ["Get", "List"]
  key_permissions    = ["Get", "List"]
}

module "users_keyvault_access_policy" {
  source             = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-key-vault-access-policy?ref=v1.0.0"
  key_vault_id       = data.azurerm_key_vault.key_vault.id
  tenant_id          = data.azurerm_client_config.current.tenant_id
  for_each           = local.all_users
  object_id          = each.value
  key_permissions    = ["Get", "List", "Update", "Create", "Delete", "Recover", "Backup", "Restore"]
  secret_permissions = ["Backup", "Delete", "Get", "List", "Purge", "Recover", "Restore", "Set"]
}

resource "azurerm_monitor_diagnostic_setting" "key_vault_audit_setting" {
  name = "eh${local.location_abbreviations[var.location]}${var.environment}${var.product}${var.short_name}akv_auditmon"
  target_resource_id = data.azurerm_key_vault.key_vault.id
  log_analytics_workspace_id = data.azurerm_log_analytics_workspace.requestrouting_law.id

  log {
    category = "AuditEvent"
    enabled  = true
  }

  metric {
    category = "AllMetrics"

    retention_policy {
      enabled = false
    }
  }
}

resource "azurerm_monitor_diagnostic_setting" "cloudsiem_key_vault_audit_setting" {
  name = "eh${local.location_abbreviations[var.location]}${var.environment}${var.product}${var.short_name}cloudsiemakvdiag"
  target_resource_id = data.azurerm_key_vault.key_vault.id
  log_analytics_workspace_id     = data.azurerm_log_analytics_workspace.cloudsiem_log_analytics_workspace.id

  log {
    category = "AuditEvent"
    enabled  = true
  }
}

resource "azurerm_key_vault_secret" "cosmosdbkey" {
  name         = "CosmosDbKey"
  value        = module.cosmosdb-account.primary_master_key
  key_vault_id = data.azurerm_key_vault.key_vault.id
}

resource "azurerm_key_vault_secret" "azsearchkey" {
  name         = "AzureSearchKey"
  value        = azurerm_search_service.request-routing-search.primary_key
  key_vault_id = data.azurerm_key_vault.key_vault.id
}

resource "azurerm_key_vault_secret" "redisconnectionstring" {
  name         = "RedisConnectionString"
  value        = module.redis.redis_cache.primary_connection_string
  key_vault_id = data.azurerm_key_vault.key_vault.id
}


observer-app-service-plan.tf
resource "azurerm_service_plan" "request_routing_observer_plan" {
  name                = "${var.environment}-asp-${var.name}-observer"
  location            = var.location
  resource_group_name = var.resource_group
  os_type             = "Linux"
  sku_name            = var.observer_sku

  timeouts {
    create = "4h"
    update = "4h"
    delete = "4h"
  }

  tags = var.tags
}

resource "azurerm_monitor_autoscale_setting" "observer_autoscale_setting" {
    name                    = "${azurerm_service_plan.request_routing_observer_plan.name}-autoscale-setting"
    resource_group_name     = var.resource_group
    location                = var.location
    target_resource_id      = azurerm_service_plan.request_routing_observer_plan.id
    enabled                 = true
    tags                    = var.tags

    profile  {
        name = "EveryDayScaling"
        capacity {
            default = 1
            minimum = 1
            maximum = 6
        }

        rule {
            metric_trigger {
                metric_name        = "CpuPercentage"
                metric_resource_id = azurerm_service_plan.request_routing_observer_plan.id
                time_grain         = "PT1M"
                statistic          = "Average"
                time_window        = "PT10M"
                time_aggregation   = "Average"
                operator           = "GreaterThan"
                threshold          = 70
            }
            scale_action {
                direction = "Increase"
                type      = "ChangeCount"
                value     = "1"
                cooldown  = "PT15M"
            }
        }
    
        rule {
            metric_trigger {
                metric_name        = "CpuPercentage"
                metric_resource_id = azurerm_service_plan.request_routing_observer_plan.id
                time_grain         = "PT1M"
                statistic          = "Average"
                time_window        = "PT10M"
                time_aggregation   = "Average"
                operator           = "LessThan"
                threshold          = 50
            }
            scale_action {
                direction = "Decrease"
                type      = "ChangeCount"
                value     = "1"
                cooldown  = "PT15M"
            }
        }
    }
}

resource "azurerm_linux_web_app" "request_routing_observer_green" {
  name                      = "${var.environment}-${var.name}-observer-green"
  location                  = var.location
  resource_group_name       = var.resource_group
  service_plan_id           = data.azurerm_service_plan.aspGreen.id
  https_only                = true
  virtual_network_subnet_id = data.azurerm_subnet.observer_vnet_integration_subnet_larger.id
  public_network_access_enabled = false

  site_config {
    app_command_line = ""
    application_stack {
      docker_image     = "globalcrep.azurecr.io/ucx/ucx-request-routing-observer-green"
      docker_image_tag = var.container_image_version
    }
    always_on                = true
    health_check_path        = "/observer/health"
    remote_debugging_enabled = false
  }

  app_settings = merge({
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE"     = "false"
    "DOCKER_REGISTRY_SERVER_URL"              = "https://globalcrep.azurecr.io"
    "DOCKER_REGISTRY_SERVER_USERNAME"         = var.container_registry_username
    "DOCKER_REGISTRY_SERVER_PASSWORD"         = var.container_registry_password
    "ApplicationInsights__InstrumentationKey" = data.azurerm_application_insights.appInsightGreen.instrumentation_key
    "APPINSIGHTS__INSTRUMENTATIONKEY"         = data.azurerm_application_insights.appInsightGreen.instrumentation_key
    "ApplicationInsights__ConnectionString"   = data.azurerm_application_insights.appInsightGreen.connection_string
    "DocumentDB__Endpoint"                    = data.azurerm_cosmosdb_account.cosmosdbGreen.endpoint
    "DocumentDB__Key"                         = var.cosmosdbkey
    "DocumentDB__DatabaseName"                = "${var.environment}requestrouting"
    "DocumentDB__EventHistoryContainer"       = var.history_container_name_green
    "DocumentDB__StateConfigurationContainer" = var.state_configuration_container_name
    "DocumentDB__UserConfigurationContainer"  = var.user_configuration_container_name
    "DocumentDB__UserProfileContainer"        = var.user_profile_container_name
    "Serilog__MinimumLevel__Default"          = var.log_level
    "SearchDB__Endpoint"                      = "https://eheu2-${var.environment}-requestrouting-acs.search.windows.net"
    "SearchDB__Key"                           = var.search_key
    "SearchDB__RoutingIndexName"              = "routing-index-green"
    "Discipline"                              = var.discipline
    "OriginSystems"                           = var.origin_systems
    "JurisdictionStatesCommercial"            = var.jurisdiction_states_commercial
    "JurisdictionStatesCommercialMedicaid"    = var.jurisdiction_states_commercial_medicaid
    "KafkaSettings__BootstrapServers"         = var.kafka_bootstrap_servers
    "KafkaSettings__GroupId"                  = "RequestRouting"
    "KafkaSettings__SecurityProtocol"         = var.kafka_security_protocol
    "KafkaSettings__SaslUsername"             = var.kafka_sasl_username
    "KafkaSettings__SaslPassword"             = var.kafka_sasl_password
    "KafkaSettings__SslCaLocation"            = var.kafka_ssl_ca_location
    "KafkaSettings__RoutableDeterminationGroupId"                = "RequestRoutableDeterminationGreen"    
    "AzureADGraphAPIClientSettings__ClientSecret"                = "@Microsoft.KeyVault(VaultName=eheu2${var.environment}ucxroutingakv;SecretName=ADClientSecret)"
    "AzureADGraphAPIClientSettings__ClientId"                    = var.ADClientId
    "AzureADGraphAPIClientSettings__Tenant"                      = var.ADTenant

    "EventReplayOffset__UCXAuthorizationCanceled"                = -1,
    "EventReplayOffset__UCXAuthorizationDismissed"               = -1,
    "EventReplayOffset__UCXAuthorizationWithdrawn"               = -1,
    "EventReplayOffset__UCXDueDateCalculated"                    = -1,
    "EventReplayOffset__UcxDueDateMissed"                        = -1,
    "EventReplayOffset__UCXDequeueRoutingRequest"                = -1,
    "EventReplayOffset__UCXFileAttached"                         = -1,
    "EventReplayOffset__UcxJurisdictionStateUpdated"             = -1,
    "EventReplayOffset__UcxMemberIneligibilitySubmitted"         = -1,
    "EventReplayOffset__UCXMemberInfoUpdatedForRequest"          = -1,
    "EventReplayOffset__UcxPeerToPeerActionSubmitted"            = -1,
    "EventReplayOffset__UcxPhysicianDecisionMade"                = -1,
    "EventReplayOffset__UCXRequestForServiceSubmitted"           = -1,
    "EventReplayOffset__UcxRequestUrgencyUpdated"                = -1,
    "EventReplayOffset__UCXRequestSpecialtyUpdated"              = -1,
    "EventReplayOffset__UcxRequestLineOfBusinessUpdated"         = -1,
    "EventReplayOffset__UCXRoutingRequestAssigned"               = -1,
    "EventReplayOffset__UcxExternalSystemStatusChange"           = -1,
    "EventReplayOffset__UCXUserExcludedFromRequest"              = -1,
    "EventReplayOffset__UCXUserProfileConfigurationUpdated"      = -1,
    "EventReplayOffset__UCXStateConfiguration"                   = -1,
    "EventReplayOffset__UcxMedicalDisciplineCorrected"           = -1,

    "MaxEventsAllowedPerRequest"                                 = 200,
    "AssignmentExpirationMinutes"                                = var.AssignmentExpirationMinutes,
    "ReplayVersion"                                              = false,
    "ReplayRoutableDetermination"                                = false,
    "CaptureRfssForRoutableDetermination"                        = true,
    "ObserverType"                                               = "Green",
    "RedisConnectionString"                                      = "@Microsoft.KeyVault(VaultName=eheu2${var.environment}ucxroutingakv;SecretName=RedisConnectionString)",
    "Locking__AcquireLockTimeout"                                = var.locking_acquire_lock_timeout,
    "Locking__ExpireLockTimeout"                                 = var.locking_expire_lock_timeout

  }, {for index, eventConfig in local.EventConfigsReplay : "EventConfigs__${index}__Name" => eventConfig.Name}, 
     {for index, eventConfig in local.EventConfigsReplay : "EventConfigs__${index}__HandlerName" =>eventConfig.HandlerName}, 
     {for index, eventConfig in local.EventConfigsReplay : "EventConfigs__${index}__Version" => eventConfig.Version}
     , merge(flatten([
      for idx, profile in local.scoring_profiles : [
        {
          for key, value in {
            "scoringProfileName"       = profile.scoringProfileName,
            "weights__Priority"        = profile.weights["Priority"],
            "weights__JurisdictionState" = profile.weights["JurisdictionState"],
            "weights__Status"          = profile.weights["Status"],
            "weights__SslRequirementStatus" = profile.weights["SslRequirementStatus"],
            "weights__AssignedToId"    = profile.weights["AssignedToId"],
          } : "SearchDB__ScoringProfiles__${idx}__${key}" => value
        },
        flatten([
          for ff_idx, ff in profile.freshnessFunctions : [
            {
              for f_key, f_value in {
                "fieldName"        = ff.fieldName,
                "boostValue"       = ff.boostValue,
                "boostingDuration" = ff.boostingDuration,
                "isFutureDated"    = ff.isFutureDated,
                "Interpolation"    = ff.Interpolation,
              } : "SearchDB__ScoringProfiles__${idx}__freshnessFunctions__${ff_idx}__${f_key}" => f_value
            }
          ]
        ])
      ]
    ])...)
    , merge(flatten([
      for idx, userconfig in local.user_groups : [
        flatten([
            {
              for f_key, f_value in {
                "Name"             = userconfig.Name,
                "UserType"         = userconfig.UserType               
              } : "UserGroups__${idx}__${f_key}" => f_value
            }
          
        ])
      ]
    ])...)
    )

  tags = var.tags

  identity {
    type = "SystemAssigned"
  }
}

resource "azurerm_private_endpoint" "observer_green_private_endpoint" {
  location            = var.location
  name                = "eheu2${var.environment}-pe-${var.name}-observer-green"
  resource_group_name = var.resource_group
  subnet_id           = data.azurerm_subnet.private-endpoint-subnet.id
  tags                = var.tags

  private_service_connection {
    name                           = "eheu2${var.environment}-psc-${var.name}"
    is_manual_connection           = false
    private_connection_resource_id = azurerm_linux_web_app.request_routing_observer_green.id
    subresource_names              = ["sites"]
  }

  private_dns_zone_group {
    name                 = "eheu2${var.environment}-zg-${var.name}"
    private_dns_zone_ids = [
      "/subscriptions/a0c6645e-c3da-4a78-9ef6-04ab6aad45ff/resourceGroups/pd1_rsg_private_dns/providers/Microsoft.Network/privateDnsZones/privatelink.azurewebsites.net"
    ]
  }
}

data "azurerm_monitor_diagnostic_categories" "diag_cat_observer_green" {
  depends_on  = [azurerm_linux_web_app.request_routing_observer_green]
  resource_id = azurerm_linux_web_app.request_routing_observer_green.id
}

resource "azurerm_monitor_diagnostic_setting" "observer_audit_setting_green" {
  depends_on                 = [data.azurerm_monitor_diagnostic_categories.diag_cat_observer_green]
  name                       = "eh${local.location_abbreviations[var.location]}${var.environment}${var.product}${var.short_name}sobsdiag"
  target_resource_id         = azurerm_linux_web_app.request_routing_observer_green.id
  log_analytics_workspace_id = data.azurerm_log_analytics_workspace.requestrouting_law.id

  metric {
    category = "AllMetrics"

    retention_policy {
      enabled = false
    }
  }
}

resource "azurerm_monitor_diagnostic_setting" "observer_cloud_audit_setting_green" {
  depends_on                 = [data.azurerm_monitor_diagnostic_categories.diag_cat_observer_green]
  name                       = "eh${local.location_abbreviations[var.location]}${var.environment}${var.product}${var.short_name}cloudsiemsobsdiag"
  target_resource_id         = azurerm_linux_web_app.request_routing_observer_green.id
  log_analytics_workspace_id = data.azurerm_log_analytics_workspace.cloudsiem_log_analytics_workspace.id

  dynamic "log" {
    iterator = log_category
    for_each = data.azurerm_monitor_diagnostic_categories.diag_cat_observer_green.logs

    content {
      category = log_category.value
      enabled  = true
    }
  }
}

locals {
  EventConfigsReplay = [
    {
      "Name"        = "StateConfigurationAdded",
      "HandlerName" = "StateConfigurationAddedHandler",
      "Version"     = "1.0.0.0"
    },
    {
      "Name"        = "UCXAuthorizationCanceled",
      "HandlerName" = "AuthorizationCanceledHandler",
      "Version"     = "1.0.0.1"
    },
    {
      "Name"        = "UCXAuthorizationDismissed",
      "HandlerName" = "AuthorizationDismissedHandler",
      "Version"     = "1.0.0.1"
    },
    {
      "Name"        = "UCXAuthorizationWithdrawn",
      "HandlerName" = "AuthorizationWithdrawnHandler",
      "Version"     = "1.0.0.1"
    },
    {
      "Name"        = "UCXDueDateCalculated",
      "HandlerName" = "DueDateCalculatedHandler",
      "Version"     = "1.1.0"
    },
    {
      "Name"        = "UcxDueDateMissed",
      "HandlerName" = "DueDateMissedHandler",
      "Version"     = "1.1.0"
    },
    {
      "Name"        = "UCXDequeueRoutingRequest",
      "HandlerName" = "DequeueRoutingRequestHandler",
      "Version"     = "1.0.0"
    },
    {
      "Name"        = "UCXFileAttached",
      "HandlerName" = "FileAttachedHandler",
      "Version"     = "1.1.0"
    },
    {
      "Name"        = "UcxJurisdictionStateUpdated",
      "HandlerName" = "JurisdictionStateUpdatedHandler",
      "Version"     = "1.2.0"
    },
    {
      "Name"        = "UcxMemberIneligibilitySubmitted",
      "HandlerName" = "MemberIneligibilitySubmittedHandler",
      "Version"     = "1.0.0.0"
    },
    {
      "Name"        = "UCXMemberInfoUpdatedForRequest",
      "HandlerName" = "MemberInfoUpdatedForRequestHandler",
      "Version"     = "1.0.0.0"
    },
    {
      "Name"        = "UcxPeerToPeerActionSubmitted",
      "HandlerName" = "PeerToPeerActionSubmittedHandler",
      "Version"     = "1.0.0.0"
    },
    {
      "Name"        = "UcxPhysicianDecisionMade",
      "HandlerName" = "PhysicianDecisionMadeHandler",
      "Version"     = "1.4.0"
    },
    {
      "Name"        = "UCXRequestForServiceSubmitted",
      "HandlerName" = "RequestForServiceSubmittedHandler",
      "Version"     = "1.4.0"
    },
    {
      "Name"        = "UcxRequestUrgencyUpdated",
      "HandlerName" = "RequestUrgencyUpdatedHandler",
      "Version"     = "1.1.0"
    },
    {
      "Name"        = "UCXRequestSpecialtyUpdated",
      "HandlerName" = "RequestSpecialtyUpdatedHandler",
      "Version"     = "1.0.0"
    },
    {
      "Name"        = "UcxRequestLineOfBusinessUpdated",
      "HandlerName" = "RequestLineOfBusinessUpdatedHandler",
      "Version"     = "1.2.0"
    },
    {
      "Name"        = "UcxExternalSystemStatusChange",
      "HandlerName" = "StatusChangeHandler",
      "Version"     = "1.0.2"
    },
    {
      "Name"        = "UserProfileConfigurationUpdated",
      "HandlerName" = "UserProfileConfigurationUpdatedHandler",
      "Version"     = "2.0.0.0"
    },
    {
      "Name"        = "UCXUserExcludedFromRequest",
      "HandlerName" = "UserExcludedFromRequestHandler",
      "Version"     = "1.0.0"
    },
    {
      "Name"        = "UCXRoutingRequestAssigned",
      "HandlerName" = "RoutingRequestAssignedHandler",
      "Version"     = "1.1.0"
    },
    {
      "Name"        = "UcxMedicalDisciplineCorrected",
      "HandlerName" = "MedicalDisciplineCorrectedHandler",
      "Version"     = "1.0.0"
    }
  ]
}


app-insights.tf
module "app_insights" {
  source              = "git::ssh://git@ssh.dev.azure.com/v3/eviCoreDev/Terraform%20Modules/ep-application-insights?ref=v1.2.0"
  environment         = var.environment
  location            = var.location
  domain              = var.name
  resource_group_name = var.resource_group
  application_type    = "web"
  retention_in_days   = var.retention_in_days
  tier                = 1
  cost_center         = var.cost_center
  asset_owner         = var.asset_owner
  business_owner      = var.business_owner
  data_classification = var.data_classification
  app_name            = var.name
  it_sponsor          = var.it_sponsor
  tags                = var.tags
}

resource "azapi_update_resource" "ApplicationInsights_AppTraces_table" {
  name      = "AppTraces"
  count     = "${var.environment == "dev" ? 1 : 0 }"
  parent_id =  data.azurerm_log_analytics_workspace.requestrouting_law.id 
  type      = "Microsoft.OperationalInsights/workspaces/tables@2022-10-01"
  body = jsonencode(
    {
      "properties" : {
        "plan" : "Basic",
        "retentionInDays"      = -1,
        "totalRetentionInDays" = -1
      }
    }
  )
}
resource "azapi_update_resource" "ApplicationInsights_MultipleTables" {
  for_each = toset(var.applicationinsights_tablenames)
  name     = each.key
  parent_id =  data.azurerm_log_analytics_workspace.requestrouting_law.id 
  type      = "Microsoft.OperationalInsights/workspaces/tables@2022-10-01"
  body = jsonencode(
    {
      "properties" : {
        "retentionInDays"      = var.interactiveretention_in_days,
        "totalRetentionInDays" = var.totalretention_in_days
      }
    }
  )
}

resource "azapi_update_resource" "ApplicationInsights_AppTraces_tableForNonDevEnv" {
  count     = "${var.environment == "dev" ? 0 : 1 }"
  name     = "AppTraces"
  parent_id =  data.azurerm_log_analytics_workspace.requestrouting_law.id 
  type      = "Microsoft.OperationalInsights/workspaces/tables@2022-10-01"
  body = jsonencode(
    {
      "properties" : {
        "retentionInDays"      = var.interactiveretention_in_days,
        "totalRetentionInDays" = var.totalretention_in_days
      }
    }
  )
}

application-gateway.tf
resource "azurerm_application_gateway" "ucx-routing-appgateway" {
  depends_on = [
    azurerm_subnet_network_security_group_association.nsg_assoc, azurerm_subnet_route_table_association.rt_assoc
  ]
  name                = "${var.environment}-${var.product}-${var.name}-appgateway"
  resource_group_name = var.resource_group
  location            = var.location

  sku {
    name = "WAF_v2"
    tier = "WAF_v2"
  }

  autoscale_configuration {
    min_capacity = var.app_gateway_sku_min_capacity
    max_capacity = var.app_gateway_sku_max_capacity
  }

  dynamic "waf_configuration" {
    for_each = local.app_gateway_waf_configurations != {} ? local.app_gateway_waf_configurations : {}
    content {
      enabled           = waf_configuration.value["gateway_waf_enabled"]
      firewall_mode     = waf_configuration.value["gateway_waf_firewall_mode"]
      rule_set_type     = waf_configuration.value["gateway_waf_rule_set_type"]
      rule_set_version  = waf_configuration.value["gateway_waf_rule_set_version"]
    }
  }

  dynamic "ssl_certificate" {
    for_each = local.app_gateway_ssl_certificate
    content {
      name     = ssl_certificate.value["AKV_CERTIFICATE_NAME"]
      data     = data.azurerm_key_vault_secret.data_secret[ssl_certificate.key].value
      password = data.azurerm_key_vault_secret.data_secret[ssl_certificate.value["AKV_CERTIFICATE_SECRET"]].value
    }
  }

  ssl_policy {
    policy_type = "Predefined"
    policy_name = "AppGwSslPolicy20170401S"
  }

  gateway_ip_configuration {
    name      = "${var.product}-${var.name}-gateway-ip-configuration"
    subnet_id = data.azurerm_subnet.appgateway_subnet.id
  }

  dynamic "frontend_port" {
    for_each = local.app_gateway_frontend_port
    content {
      name = frontend_port.key
      port = frontend_port.value["FRONTEND_PORT_NUMBER"]
    }
  }

  frontend_ip_configuration {
    name                          = "appGatewayPrivateFrontendIP"
    private_ip_address            = var.app_gateway_private_frontend_ip
    subnet_id                     = data.azurerm_subnet.appgateway_subnet.id
    private_ip_address_allocation = "Static"
  }

  frontend_ip_configuration {
    name                 = "appGatewayFrontendIP"
    public_ip_address_id = azurerm_public_ip.appgateway_pip.id
  }

  dynamic "backend_address_pool" {
    for_each = local.app_gateway_backend_address_pool
    content {
      fqdns = backend_address_pool.value["BACKEND_ADDRESS_POOL_FQDNS"]
      name  = backend_address_pool.key
    }
  }

  dynamic "backend_http_settings" {
    for_each = local.app_gateway_backend_http_settings
    content {
      name                                = backend_http_settings.value["BACKEND_HTTP_SETTINGS_NAME"]
      cookie_based_affinity               = backend_http_settings.value["BACKEND_HTTP_SETTINGS_COOKIE_AFFINITY"]
      port                                = backend_http_settings.value["BACKEND_HTTP_SETTINGS_PORT"]
      protocol                            = backend_http_settings.value["BACKEND_HTTP_SETTINGS_PROTOCOL"]
      request_timeout                     = backend_http_settings.value["BACKEND_HTTP_SETTINGS_TIMEOUT"]
      probe_name                          = backend_http_settings.value["BACKEND_HTTP_SETTINGS_PROBE_NAME"]
      host_name                           = backend_http_settings.value["BACKEND_HTTP_SETTINGS_HOSTNAME"]
      pick_host_name_from_backend_address = backend_http_settings.value["BACKEND_HTTP_SETTINGS_HOSTNAME_FROM_BACKEND"]
      trusted_root_certificate_names      = backend_http_settings.value["BACKEND_HTTP_SETTINGS_TRUSTED_ROOT_CERTIFICATE_NAMES"]
    }
  }

  dynamic "http_listener" {
    # listeners with a firewall policy
    for_each = [
    for listener in local.app_gateway_http_listener :
    {
      NAME                           = listener.HTTP_LISTENER_NAME
      FRONTEND_IP_CONFIGURATION_NAME = listener.HTTP_LISTENER_FRONTEND_IP_CFG_NAME
      FRONTEND_PORT_NAME             = listener.HTTP_LISTENER_FRONTEND_PORT_NAME
      PROTOCOL                       = listener.HTTP_LISTENER_PROTOCOL
      HOSTNAME                       = listener.HTTP_LISTENER_HOSTNAME
      CERTIFICATE                    = listener.HTTP_LISTENER_CERTIFICATE
      REQUIRE_SNI                    = listener.HTTP_LISTENER_REQUIRE_SNI
      FIREWALL_POLICY_ID             = listener.HTTP_LISTENER_FIREWALL_POLICY_ID
    } if can(listener.HTTP_LISTENER_FIREWALL_POLICY_ID)
    ]

    content {
      name                           = http_listener.value.NAME
      frontend_ip_configuration_name = http_listener.value.FRONTEND_IP_CONFIGURATION_NAME
      frontend_port_name             = http_listener.value.FRONTEND_PORT_NAME
      protocol                       = http_listener.value.PROTOCOL
      host_name                      = http_listener.value.HOSTNAME
      ssl_certificate_name           = http_listener.value.CERTIFICATE
      require_sni                    = http_listener.value.REQUIRE_SNI
      firewall_policy_id             = http_listener.value.FIREWALL_POLICY_ID
    }
  }

  dynamic "request_routing_rule" {
    # rules for backends
    for_each = [
    for rr in local.app_gateway_routing_rule :
    {
      ROUTING_RULE_NAME          = rr.ROUTING_RULE_NAME
      ROUTING_RULE_TYPE          = rr.ROUTING_RULE_TYPE
      HTTP_LISTENER_NAME         = rr.HTTP_LISTENER_NAME
      BACKEND_ADDRESS_POOL_NAME  = rr.BACKEND_ADDRESS_POOL_NAME
      BACKEND_HTTP_SETTINGS_NAME = rr.BACKEND_HTTP_SETTINGS_NAME
      URL_PATH_MAP_NAME          = rr.URL_PATH_MAP_NAME
      PRIORITY                   = rr.PRIORITY

    } if rr.BACKEND_ADDRESS_POOL_NAME != ""
    ]

    content {
      name                       = request_routing_rule.value.ROUTING_RULE_NAME
      rule_type                  = request_routing_rule.value.ROUTING_RULE_TYPE
      http_listener_name         = request_routing_rule.value.HTTP_LISTENER_NAME
      backend_address_pool_name  = request_routing_rule.value.BACKEND_ADDRESS_POOL_NAME
      backend_http_settings_name = request_routing_rule.value.BACKEND_HTTP_SETTINGS_NAME
      url_path_map_name          = request_routing_rule.value.URL_PATH_MAP_NAME
      priority                   = request_routing_rule.value.PRIORITY
    }
  }

  dynamic "probe" {
    for_each = local.app_gateway_probe
    content {
      name                                      = probe.key
      protocol                                  = probe.value["PROBE_PROTOCOL"]
      path                                      = probe.value["PROBE_PATH"]
      interval                                  = probe.value["PROBE_INTERVAL"]
      timeout                                   = probe.value["PROBE_TIMEOUT"]
      unhealthy_threshold                       = probe.value["PROBE_UNHEALTHY_THRESHOLD"]
      pick_host_name_from_backend_http_settings = probe.value["PROBE_PICK_HOSTNAME_FROM_BACKEND"]
      minimum_servers                           = probe.value["PROBE_MINIMUM_SERVERS"]
      match {
        body        = probe.value["PROBE_MATCH_BODY"]
        status_code = probe.value["PROBE_MATCH_STATUS_CODE"]
      }
    }
  }

  dynamic "redirect_configuration" {
    for_each = local.app_gateway_redirect_configuration
    content {
      name                 = redirect_configuration.key
      redirect_type        = redirect_configuration.value["REDIRECT_CONFIGURATION_REDIRECT_TYPE"]
      include_path         = redirect_configuration.value["REDIRECT_CONFIGURATION_INCLUDE_PATH"]
      include_query_string = redirect_configuration.value["REDIRECT_CONFIGURATION_INCLUDE_QUERY_STRING"]
      target_listener_name = redirect_configuration.value["REDIRECT_CONFIGURATION_TARGET_LISTENER_NAME"]
    }
  }

  dynamic "redirect_configuration" {
    for_each = local.app_gateway_redirect_configuration_target
    content {
      name                 = redirect_configuration.key
      redirect_type        = redirect_configuration.value["REDIRECT_CONFIGURATION_REDIRECT_TYPE"]
      include_path         = redirect_configuration.value["REDIRECT_CONFIGURATION_INCLUDE_PATH"]
      include_query_string = redirect_configuration.value["REDIRECT_CONFIGURATION_INCLUDE_QUERY_STRING"]
      target_url           = redirect_configuration.value["REDIRECT_CONFIGURATION_TARGET_URL"]
    }
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [
      timeouts
    ]
  }
}

resource "azurerm_public_ip" "appgateway_pip" {
  name                = "${var.product}-${var.name}-appgateway-publicIP"
  resource_group_name = var.resource_group
  location            = var.location
  allocation_method   = "Static"
  sku                 = "Standard"
  zones               = ["1", "2", "3"]
  tags                = var.tags
}

resource "azurerm_monitor_diagnostic_setting" "diagnostic_setting" {
  depends_on                 = [azurerm_application_gateway.ucx-routing-appgateway, module.app_insights]
  name                       = "${var.environment}${var.product}${var.name}appgatewaydiagnosticsetting"
  target_resource_id         = azurerm_application_gateway.ucx-routing-appgateway.id
  log_analytics_workspace_id = data.azurerm_log_analytics_workspace.requestrouting_law.id

  dynamic "log" {
    for_each = local.monitor_app_gateway_diagnostic_log_category

    content {
      category = log.key
      enabled  = log.value["DIAGNOSTIC_LOG_ENABLED"]

      retention_policy {
        days    = 0
        enabled = false
      }
    }
  }

  dynamic "metric" {
    for_each = local.monitor_app_gateway_diagnostic_metric_category

    content {
      category = metric.key
      enabled  = metric.value["DIAGNOSTIC_METRIC_ENABLED"]

      retention_policy {
        days    = 0
        enabled = false
      }
    }
  }

  timeouts {
    create = "2h"
    update = "2h"
    delete = "2h"
  }

  lifecycle {
    ignore_changes = [

    ]
  }

}

resource "azurerm_monitor_diagnostic_setting" "cloudsiem_diagnostic_setting" {
  depends_on                 = [azurerm_application_gateway.ucx-routing-appgateway, module.app_insights]
  name                       = "${var.environment}${var.product}${var.name}appgatewaycloudsiemdiagnosticsetting"
  target_resource_id         = azurerm_application_gateway.ucx-routing-appgateway.id
  log_analytics_workspace_id     = data.azurerm_log_analytics_workspace.cloudsiem_log_analytics_workspace.id

  dynamic "log" {
    for_each = local.monitor_app_gateway_diagnostic_log_category

    content {
      category = log.key
      enabled  = log.value["DIAGNOSTIC_LOG_ENABLED"]

      retention_policy {
        days    = 0
        enabled = false
      }
    }
  }

  timeouts {
    create = "2h"
    update = "2h"
    delete = "2h"
  }

  lifecycle {
    ignore_changes = [

    ]
  }
}

data "azurerm_key_vault" "data_apphub_akv" {
  provider            = azurerm.key_vault_sub
  name                = var.shared_kv_name
  resource_group_name = var.shared_kv_rsg
}

data "azurerm_key_vault_secret" "data_secret" {
  provider     = azurerm.key_vault_sub
  for_each     = local.shared_kv_secret
  name         = each.key
  key_vault_id = data.azurerm_key_vault.data_apphub_akv.id
}

application-gateway-nsg.tf
resource "azurerm_network_security_group" "ucx-routing-appgateway-nsg" {
  name                = format("%s%s%s", var.name, "-appgateway-", "nsg")
  location            = var.location
  resource_group_name = var.vnet_resource_group

  tags = var.tags

  lifecycle {
    ignore_changes = [
      tags ["createdBy"],
      tags ["CreatedDate"],
      tags ["createdDateTime"],
      tags ["Environment"],
    ]
  }

  security_rule {
    name                       = "inbound_allow_tcp_80_http"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "80"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "inbound_allow_tcp_443_https"
    priority                   = 110
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "inbound_allow_any_65200-65535_azure_infrastructure"
    priority                   = 120
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "65200-65535"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }
}

resource "azurerm_subnet_network_security_group_association" "nsg_assoc" {
  subnet_id                 = data.azurerm_subnet.appgateway_subnet.id
  network_security_group_id = azurerm_network_security_group.ucx-routing-appgateway-nsg.id
}

application-gateway-rt.tf
resource "azurerm_route_table" "ucx-routing-appgateway-rt" {
  name                          = format("%s%s%s%s", var.location, "-", var.name, "-appgateway-default-internet-rt")
  location                      = var.location
  resource_group_name           = var.vnet_resource_group
  disable_bgp_route_propagation = false

  route {
    name           = "FirewallDefaultRoute"
    address_prefix = "0.0.0.0/0"
    next_hop_type  = "Internet"
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [
      tags ["createdBy"],
      tags ["CreatedDate"],
      tags ["createdDateTime"],
      tags ["Environment"],
    ]
  }
}

resource "azurerm_subnet_route_table_association" "rt_assoc" {
  subnet_id      = data.azurerm_subnet.appgateway_subnet.id
  route_table_id = azurerm_route_table.ucx-routing-appgateway-rt.id
}

application-gateway-waf-policy.tf
resource "azurerm_web_application_firewall_policy" "waf_policy" {
  name                = "${var.name}_listener_${var.environment}_waf_policy_internal"
  resource_group_name = var.resource_group
  location            = var.location

  policy_settings {
    enabled                     = true
    file_upload_limit_in_mb     = 100
    max_request_body_size_in_kb = 2000
    mode                        = "Prevention"
    request_body_check          = true
  }

  managed_rules {
    managed_rule_set {
      type                = "OWASP"
      version             = "3.2"
    }
  }

  tags = var.tags

  lifecycle {
    ignore_changes = []
  }
}

Dev.tfvars
// general
subscription_id                         = "a01410e2-23c4-49c7-818e-04a337a3638e"
environment                             = "dev"
resource_group                          = "dev_rsg_eu2_requestrouting"
log_level                               = "Debug"
shared_kv_sub_id                        = "5fab10df-31da-4b2f-a1a8-4e2f514b07f0"
shared_kv_name                          = "eheu2dv1akv"
shared_kv_rsg                           = "dv1_rsg_apphub"
cloudsiem_log_analytics_subscription_id = "a0c6645e-c3da-4a78-9ef6-04ab6aad45ff"
cloudsiem_log_analytics_resource_group  = "pd1_rsg_eu2_security_siem"
cloudsiem_log_analytics_name            = "pd1-cloudsiem"
environmentPrefix                       = "dv1"
appinsightNameGreen                      = "eheu2dev-ai-requestrouting"
cosmosdbNameGreen                        = "eheu2devcdbucxrequestrouting"

// networking
vnet_resource_group = "vnet-requestrouting-eu2-dev"
vnet_cidr           = ["10.193.149.0/25","10.196.102.192/26"]
subnets             = {
  "eu2-dev-private-endpoint" = {
    address_prefixes                          = ["10.193.149.0/28"]
    private_endpoint_network_policies_enabled = false
    managednsg                                = false
    managedroutetable                         = false
  }
  "eu2-dev-api-vnet-integration" = {
    address_prefixes                          = ["10.193.149.16/28"]
    private_endpoint_network_policies_enabled = true
    service_endpoints                         = [
      "Microsoft.KeyVault", "Microsoft.AzureCosmosDB", "Microsoft.CognitiveServices"
    ]
    managednsg                 = false
    managedroutetable          = false
    delegation_name            = "appServiceDelegation"
    service_delegation_name    = "Microsoft.Web/serverFarms"
    service_delegation_actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
  }
  "eu2-dev-observer-vnet-integration" = {
    address_prefixes                          = ["10.193.149.32/27"]
    private_endpoint_network_policies_enabled = true
    service_endpoints                         = [
      "Microsoft.KeyVault", "Microsoft.AzureCosmosDB", "Microsoft.CognitiveServices"
    ]
    managednsg                 = false
    managedroutetable          = false
    delegation_name            = "appServiceDelegation"
    service_delegation_name    = "Microsoft.Web/serverFarms"
    service_delegation_actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
  }
  "eu2-dev-application-gateway" = {
    address_prefixes                          = ["10.193.149.64/29"]
    private_endpoint_network_policies_enabled = true
    managednsg                                = true
    managedroutetable                         = true
  }
  "eu2-dev-private-endpoint1" = {
    address_prefixes                          = ["10.193.149.72/29"]
    private_endpoint_network_policies_enabled = false
    managednsg                                = false
    managedroutetable                         = true
  }
}
private_endpoint_subnet_name          = "requestrouting-eu2-dev-private-endpoint-subnet"
private_endpoint1_subnet_name         = "requestrouting-eu2-dev-private-endpoint1-subnet"
api_vnet_integration_subnet_name      = "requestrouting-eu2-dev-api-vnet-integration-subnet"
observer_vnet_integration_subnet_name = "requestrouting-eu2-dev-observer-vnet-integration-subnet"
observer_vnet_integration_larger_subnet_name = "requestrouting-eu2-dev-observer-vnet-integration-larger-subnet"
appgateway_subnet_name                = "requestrouting-eu2-dev-application-gateway-subnet"
app_gateway_private_frontend_ip       = "10.193.149.68"

// b2c
b2c_tenant              = "ehdv1b2c"
b2c_tenant_id           = "6c86df74-18fb-4b36-86b7-a3492115b909"
b2c_client_id           = "ba48bf26-80d5-4c77-b9aa-ac7a865dcb79"
b2c_ucx_client_id       = "9f3eb1b4-c333-47ef-ad11-44745710b07e"
b2c_ucx_api_client_id   = "5f3a640a-613f-4696-afff-5c740cc371cf"
b2c_groups_ucx_md_group = "dv1_ucx_md,dv1_EpSleepMedicalDirector"
b2c_groups_developer    = "dv1_rsg_ucx_contributor"

// acs
search_sku      = "basic"
search_replicas = 1

// api
api_sku = "S1"

// observer
observer_sku = "S2"

locking_acquire_lock_timeout = "00:30:00"
locking_expire_lock_timeout  = "00:15:00"

// application
discipline     = "Sleep,Gastro,DME,Radiology,Cardiology,MSK"
origin_systems = "eP,IOne,UCX"
jurisdiction_states_commercial = "ak,mo,ct,mn"
jurisdiction_states_commercial_medicaid = "mn,az"

// kafka
kafka_bootstrap_servers = "eheu2dv1cpkaf01.innovate.lan:9093,eheu2dv1cpkaf02.innovate.lan:9093,eheu2dv1cpkaf03.innovate.lan:9093,eheu2dv1cpkaf04.innovate.lan:9093,eheu2dv1cpkaf05.innovate.lan:9093"
kafka_sasl_password     = "@Microsoft.KeyVault(SecretUri=https://eheu2devucxroutingakv.vault.azure.net/secrets/KafkaPassword)"
kafka_ssl_ca_location   = "/app/DevCARoot.crt"

// cosmos key 
cosmosdbkey = "@Microsoft.KeyVault(SecretUri=https://eheu2devucxroutingakv.vault.azure.net/secrets/CosmosDbKey)"
cosmos_history_container_max_throughput = 4000
cosmos_cold_history_container_max_throughput = 1000
cosmos_max_throughput = 4000

// Azure Search Key
search_key = "@Microsoft.KeyVault(SecretUri=https://eheu2devucxroutingakv.vault.azure.net/secrets/AzureSearchKey)"

// LaunchDarkly
ld_sdk_key="sdk-381f10ea-e48b-4470-a36b-d06692c909af"

  
//Azure Log Analytics data retention parameter settings   
retention_in_days = 30
applicationinsights_tablenames = [
        "AppAvailabilityResults",
        "AppBrowserTimings",
        "AppDependencies",
        "AppEvents",
        "AppMetrics",
        "AppPageViews" ,
        "AppPerformanceCounters",
        "AppRequests",
        "AppSystemEvents",
        "AppExceptions"
    ]
BasicPlan_tablestorage_totalretention_in_days = 8
interactiveretention_in_days                  = 30
totalretention_in_days                        = 30


Thanks and Regards
Siraj

