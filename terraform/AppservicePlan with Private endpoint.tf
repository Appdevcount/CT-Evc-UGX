terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">=3.13.0"
    }
  }
}

locals {
  location_abbreviations = {
    eastus         = "eu1"
    eastus2        = "eu2"
    westus         = "wu1"
    westus2        = "wu2"
    centralus      = "cus"
    southcentralus = "scu"
  }
  name                  = "eh${local.location_abbreviations[var.location]}${var.environment}-as-${var.domain}-${var.role}"
  private_endpoint_name = "eh${local.location_abbreviations[var.location]}${var.environment}-pe-${var.domain}-${var.role}"
  default_documents     = ["Default.htm", "Default.html", "Default.asp", "index.htm", "index.html", "iisstart.htm", "default.aspx", "index.php", "hostingstart.html"]
  docker_image          = "${var.docker_settings.registry}/${var.container_image.name}"
  docker_settings = {
    "DOCKER_REGISTRY_SERVER_URL"      = "https://${var.docker_settings.registry}"
    "DOCKER_REGISTRY_SERVER_USERNAME" = var.docker_settings.username
    "DOCKER_REGISTRY_SERVER_PASSWORD" = var.docker_settings.password
  }

  merged_app_settings = merge(local.docker_settings, var.appsettings)
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

resource "azurerm_linux_web_app" "module" {
  lifecycle {
    create_before_destroy = true
  }

  name                          = local.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  service_plan_id               = var.service_plan_id
  https_only                    = true
  virtual_network_subnet_id     = var.vnet_integration_subnet_id
  client_certificate_enabled    = var.client_certificate_enabled
  client_certificate_mode       = var.client_certificate_mode
  public_network_access_enabled = false

  app_settings                  = local.merged_app_settings
  
  site_config {
    always_on                         = lookup(var.site_config, "always_on", "false")
    health_check_path                 = lookup(var.site_config, "health_check_path", null)
    health_check_eviction_time_in_min = lookup(var.site_config, "health_check_eviction_time_in_min", 5)
    use_32_bit_worker                 = lookup(var.site_config, "use_32_bit_worker_process", "false") # some tiers require true
    default_documents                 = lookup(var.site_config, "default_documents", local.default_documents)
    ftps_state                        = lookup(var.site_config, "ftps_state", "Disabled")
    worker_count                      = lookup(var.site_config, "worker_count", 1)
    load_balancing_mode               = var.load_balancing_mode
    http2_enabled                     = lookup(var.site_config, "http2_enabled", "false")

    application_stack {
      docker_registry_url      = "https://${var.docker_settings.registry}"
      docker_image_name        = "${var.docker_settings.registry}/${var.container_image.name}"
      docker_registry_username = var.docker_settings.username
      docker_registry_password = var.docker_settings.password
    }

    ip_restriction {
      virtual_network_subnet_id = var.vnet_integration_subnet_id
    }

    cors {
      allowed_origins       = var.allowed_origins
      support_credentials   = var.support_credentials
    }
  }

  dynamic "connection_string" {
    for_each = var.connection_strings

    content {
      name  = connection_string.value.name
      type  = connection_string.value.type
      value = connection_string.value.value
    }
  }

  identity {
    type = "SystemAssigned"
  }

  tags = local.merged_tags
}

resource "azurerm_private_endpoint" "module" {
  name                = local.private_endpoint_name
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.private_endpoint_subnet_id

  private_service_connection {
    name                           = local.name
    private_connection_resource_id = azurerm_linux_web_app.module.id
    is_manual_connection           = false
    subresource_names              = ["sites"]
  }

  tags = local.merged_tags
}

