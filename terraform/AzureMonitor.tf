resource "azurerm_monitor_autoscale_setting" "api_scaler" {
  name                = "ucx-diagnosis-api-auto-scale-cpu"
  resource_group_name = var.resource_group_name
  location            = var.location
  target_resource_id  = module.service_plan_api.id

  profile {
    name = "cpu scaler"

    capacity {
      default = 1
      minimum = var.api_auto_scale_minimum
      maximum = var.api_auto_scale_maximum
    }

    rule {
      metric_trigger {
        metric_name        = "CPUPercentage"
        metric_resource_id = module.service_plan_api.id
        time_grain         = "PT1M"
        statistic          = "Average"
        time_window        = "PT5M"
        time_aggregation   = "Average"
        operator           = "GreaterThan"
        threshold          = 75
        metric_namespace   = "Microsoft.Web/serverfarms"
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
        metric_name        = "CPUPercentage"
        metric_resource_id = module.service_plan_api.id
        time_grain         = "PT1M"
        statistic          = "Average"
        time_window        = "PT5M"
        time_aggregation   = "Average"
        operator           = "LessThan"
        threshold          = 33
      }

      scale_action {
        direction = "Decrease"
        type      = "ChangeCount"
        value     = "1"
        cooldown  = "PT5M"
      }
    }
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




resource "azurerm_monitor_metric_alert" "app_service_failures" {
  name                = "${local.name}-failure-alerts"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_linux_web_app.module.id]
  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
        action_group_id = action.value
    }
  }

  criteria {
    metric_namespace = "Microsoft.Web/sites"
    metric_name      = "Http5xx"
    aggregation      = "Count"
    operator         = "GreaterThan"
    threshold        = var.failure_alert_settings.threshold
  }

  frequency   = var.failure_alert_settings.frequency
  window_size = var.failure_alert_settings.window_size
  severity    = 2

  tags = local.merged_tags
}

resource "azurerm_monitor_metric_alert" "app_service_health" {
  count               = lookup(var.site_config, "health_check_path", null) != null ? 1 : 0
  name                = "${local.name}-availability-alerts"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_linux_web_app.module.id]
  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
        action_group_id = action.value
    }
  }

  criteria {
    metric_namespace = "Microsoft.Web/sites"
    metric_name      = "HealthCheckStatus"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = 100
  }

  severity = 0

  tags = local.merged_tags
}

resource "azurerm_monitor_metric_alert" "http_response_time" {
  name                = "${local.name}-http-response-time-alerts"
  resource_group_name = var.resource_group_name
  scopes              = [azurerm_linux_web_app.module.id]

  dynamic_criteria {
    metric_namespace  = "Microsoft.Web/sites"
    metric_name       = "HttpResponseTime"
    aggregation       = "Average"
    operator          = "GreaterThan"
    alert_sensitivity = var.response_alert_settings.alert_sensitivity
  }

  dynamic "action" {
    for_each = var.monitor_action_group_ids
    content {
        action_group_id = action.value
    }
  }

  frequency   = var.response_alert_settings.frequency
  window_size = var.response_alert_settings.window_size
  severity    = var.response_alert_settings.severity

  tags = local.merged_tags
}

