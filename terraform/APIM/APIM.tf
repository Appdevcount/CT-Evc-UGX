
data "azurerm_api_management" "apim" {
  name                = var.apim_name
  provider            = azurerm.apim_provider
  resource_group_name = var.apim_resource_group
}

data "azurerm_api_management_product" "ucx_token_authorization" {
  product_id          = "UCX-JWT"
  provider            = azurerm.apim_provider
  api_management_name = data.azurerm_api_management.apim.name
  resource_group_name = var.apim_resource_group
}

resource "azurerm_api_management_api" "apim_api" {
  provider              = azurerm.apim_provider
  name                  = var.apim_api_name
  resource_group_name   = var.apim_resource_group
  api_management_name   = data.azurerm_api_management.apim.name
  revision              = "1"
  display_name          = "UCX-MemberHistoryAPI-JWT"
  path                  = var.apim_url_suffix
  protocols             = ["https"]
  service_url           = "https://${module.api-pvt.app_service_url}/"
  subscription_required = true
  subscription_key_parameter_names {
    header = "Ocp-Apim-Subscription-Key"
    query  = "subscription-key"
  }
}

resource "azurerm_api_management_product_api" "apim_product_mapping" {
  provider            = azurerm.apim_provider
  api_name            = azurerm_api_management_api.apim_api.name
  product_id          = data.azurerm_api_management_product.ucx_token_authorization.product_id
  api_management_name = data.azurerm_api_management.apim.name
  resource_group_name = data.azurerm_api_management.apim.resource_group_name
}

resource "azurerm_api_management_api_operation" "memberHistoryv2" {
  provider            = azurerm.apim_provider
  operation_id        = "ucx-memberhistoryapi-jwt_v2"
  api_name            = azurerm_api_management_api.apim_api.name
  api_management_name = data.azurerm_api_management.apim.name
  resource_group_name = data.azurerm_api_management.apim.resource_group_name
  display_name        = "Get Member History V2"
  method              = "GET"
  url_template        = "/api/v2/MemberHistory/{id}"
  description         = "Get the member history list V2"

  template_parameter {
    name     = "id"
    required = true
    type     = ""
  }

  request {
    header {
      name     = "X-Generating-Agent"
      type     = "string"
      required = false
    }

    header {
      name     = "X-Generating-Source"
      type     = "string"
      required = false
    }

    header {
      name     = "X-Generating-Agent-Context"
      type     = "string"
      required = false
    }

    header {
      name     = "X-Generating-Agent-DisplayName"
      type     = "string"
      required = false
    }

    header {
      name     = "X-Generating-Agent-Role"
      type     = "string"
      required = false
    }
  }

  dynamic "response" {
    for_each = var.status_codes

    content {
      status_code = response.value
    }
  }
}
resource "azurerm_api_management_api_operation_policy" "memberHistory_policyv2" {
  provider            = azurerm.apim_provider
  api_name            = azurerm_api_management_api.apim_api.name
  api_management_name = data.azurerm_api_management.apim.name
  resource_group_name = data.azurerm_api_management.apim.resource_group_name
  operation_id        = azurerm_api_management_api_operation.memberHistoryv2.operation_id
  xml_content         = templatefile("${path.module}/operation_policy.xml.tpl", local.policy_configuration_member_history_v2)
}

provider "azurerm" {
  alias   = "apim"
  features {}
}

##########################################################
# DATA
##########################################################

data "azurerm_resource_group" "resource_group_apim" {
  name = var.resource_group_name_apim
}

data "azurerm_api_management" "shared_apim" {
  name                = local.apim_name
  resource_group_name = var.resource_group_name_apim
}

data "azurerm_api_management_product" "ucx_product" {
  product_id = var.apim_product_product_id
  api_management_name = local.apim_name
  resource_group_name = var.resource_group_name_apim
}

##########################################################
# RESOURCES
##########################################################

#Install API into APIM
resource "azurerm_api_management_api" "activityLogService" {
  name                = local.apim_api_name
  resource_group_name = var.resource_group_name_apim
  api_management_name = local.apim_name
  revision            = local.apim_api_revision
  display_name        = local.apim_api_display_name
  path                = local.apim_api_path
  protocols           = [local.apim_api_protocols]
  service_url         = "https://${module.api-pvt.app_service_url}/"
  description         = local.apim_api_description

  subscription_key_parameter_names {
    header = "Ocp-Apim-Subscription-Key"
    query  = "subscription-key"
  }

  import {
    content_format = "openapi+json"
    content_value  = <<JSON
    {
      "openapi": "3.0.1",
      "info": {
        "title": "Activity Log Service",
        "version": "v1"
      },
      "paths": {
        "/ActivityLog/GetActivityLog/{authorizationKey}/{requestKey}": {
          "get": {
            "tags": [
              "ActivityLog"
            ],
            "parameters": [
              {
                "name": "authorizationKey",
                "in": "path",
                "required": true,
                "schema": {
                  "type": "string",
                  "format": "uuid"
                }
              },
              {
                "name": "requestKey",
                "in": "path",
                "required": true,
                "schema": {
                  "type": "string",
                  "format": "uuid"
                }
              }
            ],
            "responses": {
              "200": {
                "description": "Success",
                "content": {
                  "text/plain": {
                    "schema": {
                      "type": "array",
                      "items": {
                        "$ref": "#/components/schemas/ActivityLogEntry"
                      }
                    }
                  },
                  "application/json": {
                    "schema": {
                      "type": "array",
                      "items": {
                        "$ref": "#/components/schemas/ActivityLogEntry"
                      }
                    }
                  },
                  "text/json": {
                    "schema": {
                      "type": "array",
                      "items": {
                        "$ref": "#/components/schemas/ActivityLogEntry"
                      }
                    }
                  }
                }
              }
            }
          }
        },
        "/api/v1/ActivityLog/GetActivityLog/{authorizationKey}/{requestKey}": {
          "get": {
            "tags": [
              "ActivityLog"
            ],
            "parameters": [
              {
                "name": "authorizationKey",
                "in": "path",
                "required": true,
                "schema": {
                  "type": "string",
                  "format": "uuid"
                }
              },
              {
                "name": "requestKey",
                "in": "path",
                "required": true,
                "schema": {
                  "type": "string",
                  "format": "uuid"
                }
              }
            ],
            "responses": {
              "200": {
                "description": "Success",
                "content": {
                  "text/plain": {
                    "schema": {
                      "type": "array",
                      "items": {
                        "$ref": "#/components/schemas/ActivityLogEntry"
                      }
                    }
                  },
                  "application/json": {
                    "schema": {
                      "type": "array",
                      "items": {
                        "$ref": "#/components/schemas/ActivityLogEntry"
                      }
                    }
                  },
                  "text/json": {
                    "schema": {
                      "type": "array",
                      "items": {
                        "$ref": "#/components/schemas/ActivityLogEntry"
                      }
                    }
                  }
                }
              }
            }
          }
        },
        "/Health": {
          "get": {
            "tags": [
              "Health"
            ],
            "responses": {
              "200": {
                "description": "Success"
              }
            }
          }
        },
        "/api/v1/Health": {
          "get": {
            "tags": [
              "Health"
            ],
            "responses": {
              "200": {
                "description": "Success"
              }
            }
          }
        }
      },
      "components": {
        "schemas": {
          "ActivityLogEntry": {
            "type": "object",
            "properties": {
              "eventName": {
                "type": "string",
                "nullable": true
              },
              "eventTimeUtc": {
                "type": "string",
                "format": "date-time"
              },
              "userDisplayName": {
                "type": "string",
                "nullable": true
              },
              "userRole": {
                "type": "string",
                "nullable": true
              },
              "authorizationKey": {
                "type": "string",
                "format": "uuid"
              },
              "requestForServiceId": {
                "type": "string",
                "nullable": true
              },
              "requestForServiceKey": {
                "type": "string",
                "format": "uuid"
              }
            },
            "additionalProperties": false
          }
        }
      }
    }
    JSON
  }
}

# Adding a Policy for all endpoints within the activityLogService API
resource "azurerm_api_management_api_policy" "activityLogService" {
  api_name            = azurerm_api_management_api.activityLogService.name
  api_management_name = azurerm_api_management_api.activityLogService.api_management_name
  resource_group_name = azurerm_api_management_api.activityLogService.resource_group_name

  xml_content = <<XML
    <policies>
        <inbound>
            <base />
            <validate-jwt header-name="Authorization" failed-validation-httpcode="403" failed-validation-error-message="Forbidden Access" require-scheme="Bearer">
                <openid-config url="${var.apim_policy_openid-config}" />
                <required-claims>
                    <claim name="groups" match="any">
                        <value>${var.b2c_medical_director_group_name}</value>
                        <value>${var.b2c_nurse_group_name}</value>
                        <value>${var.b2c_ucx_md_group_name}</value>
                    </claim>
                </required-claims>
            </validate-jwt>
        </inbound>
        <backend>
            <base />
        </backend>
        <outbound>
            <base />
        </outbound>
        <on-error>
            <base />
        </on-error>
    </policies>
    XML
}

resource "azurerm_api_management_product_api" "ucx_product_api" {
  api_name = azurerm_api_management_api.activityLogService.name
  product_id = data.azurerm_api_management_product.ucx_product.product_id
  api_management_name = data.azurerm_api_management.shared_apim.name
  resource_group_name = data.azurerm_resource_group.resource_group_apim.name
}

##########################################################
# OUTPUTS
##########################################################

output "activityLogService_api_resource_group_name" {
  value = data.azurerm_api_management.shared_apim.resource_group_name
}

output "apim_resource_group_name" {
  value = data.azurerm_resource_group.resource_group_apim.name
}

output "apim_resource_group_id" {
  value = data.azurerm_resource_group.resource_group_apim.id
}

output "apim_resource_group_location" {
  value = data.azurerm_resource_group.resource_group_apim.location
}

output "apim_id" {
  value = data.azurerm_api_management.shared_apim.id
}

output "apim_name" {
  value = data.azurerm_api_management.shared_apim.name
}


data "azurerm_api_management" "apim" {
  name                = var.apim_name
  provider            = azurerm.apim_provider
  resource_group_name = var.apim_resource_group
}

data "azurerm_resource_group" "resource_group_apim" {
  name = var.apim_resource_group 
}

resource "azurerm_api_management_api" "apim_api" {
  provider              = azurerm.apim_provider
  name                  = var.apim_api_name
  resource_group_name   = var.apim_resource_group
  api_management_name   = data.azurerm_api_management.apim.name
  revision              = "1"
  display_name          = "UPADSService.Api"
  path                  = var.apim_url_suffix
  protocols             = ["https"]
  service_url           = "https://${module.api.app_service_url}/"
  subscription_required = true
  subscription_key_parameter_names {
    header = "Ocp-Apim-Subscription-Key"
    query  = "subscription-key"
  }

  import {
    content_format = "openapi+json"
    content_value  = <<JSON
    {
      "openapi": "3.0.1",
      "info": {
        "title": "UPADSService.Api",
        "version": "1.0"
      },
      "paths": {
        "/Upads?pathwayName={pathwayName}&UserName={userName}&DisplayName={displayName}&Context={context}&AdRoleName={adRoleName}&GenericRole={genericRole}&authorizationKey={authorizationKey}&requestType={requestType}": {
          "get": {
            "tags": [
              "UpadsService"
            ],
            "parameters": [
              {
                "name": "pathwayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "userName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "displayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "context",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "adRoleName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "genericRole",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "authorizationKey",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "requestType",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              }
            ],
            "responses": {
              "200": {
                "description": "Success"
              }
            }
          }
        },
        "/api/v1/Upads?pathwayName={pathwayName}&UserName={userName}&DisplayName={displayName}&Context={context}&AdRoleName={adRoleName}&GenericRole={genericRole}&authorizationKey={authorizationKey}&requestType={requestType}": {
          "get": {
            "tags": [
              "UpadsService"
            ],
            "parameters": [
              {
                "name": "pathwayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "userName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "displayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "context",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "adRoleName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "genericRole",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "authorizationKey",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "requestType",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              }
            ],
            "responses": {
              "200": {
                "description": "Success"
              }
            }
          }
        },
        "/Upads?UserName={userName}&DisplayName={displayName}&Context={context}&AdRoleName={adRoleName}&GenericRole={genericRole}&pathwayName={pathwayName}&authorizationKey={authorizationKey}&requestType={requestType}": {
          "post": {
            "tags": [
              "UpadsService"
            ],
            "parameters": [
              {
                "name": "userName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "displayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "context",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "adRoleName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "genericRole",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "pathwayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "authorizationKey",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "requestType",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              }
            ],
            "responses": {
              "200": {
                "description": "Success"
              }
            }
          }
        },
        "/api/v1/Upads?UserName={userName}&DisplayName={displayName}&Context={context}&AdRoleName={adRoleName}&GenericRole={genericRole}&pathwayName={pathwayName}&authorizationKey={authorizationKey}&requestType={requestType}": {
          "post": {
            "tags": [
              "UpadsService"
            ],
            "parameters": [
              {
                "name": "userName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "displayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "context",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "adRoleName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "genericRole",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "pathwayName",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "authorizationKey",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              },
              {
                "name": "requestType",
                "in": "query",
                "required": true,
                "schema": {
                  "type": "string",
                  "nullable": true
                }
              }
            ],
            "responses": {
              "200": {
                "description": "Success"
              }
            }
          }
        }
      },
      "components": { }
    }
    JSON
  }

}

# Adding a Policy for all endpoints within the UPADSService API
resource "azurerm_api_management_api_policy" "UPADSService" {
  api_name            = azurerm_api_management_api.apim_api.name
  api_management_name = azurerm_api_management_api.apim_api.api_management_name
  resource_group_name = azurerm_api_management_api.apim_api.resource_group_name

  xml_content = <<XML
    <policies>
        <inbound>
            <base />
            <validate-jwt header-name="Authorization" failed-validation-httpcode="401" failed-validation-error-message="Unauthorized. Access token is missing or invalid.">
                <openid-config url="${var.apim_policy_openid-config}" />
                <openid-config url="${var.apim_policy_openid-config_ep}" />
                <required-claims>
                    <claim name="groups" match="any">
                        <value>${var.b2c_medical_director_group_name}</value>
                        <value>${var.b2c_nurse_group_name}</value>
                        <value>${var.b2c_fertility_advisor_group_name}</value>
                        <value>${var.b2c_pac_medical_director_group_name}</value>
                    </claim>
                </required-claims>
            </validate-jwt>
        </inbound>
        <backend>
            <base />
        </backend>
        <outbound>
            <base />
        </outbound>
        <on-error>
            <base />
        </on-error>
    </policies>
    XML
}

resource "azurerm_api_management_api_operation_policy" "UPADSService_policy" {
  provider            = azurerm.apim_provider
  api_name            = azurerm_api_management_api.apim_api.name
  api_management_name = azurerm_api_management_api.apim_api.api_management_name
  resource_group_name = azurerm_api_management_api.apim_api.resource_group_name
  operation_id        = "get-upads-pathwayname-pathwayname-username-username-displayname-displayname"
    xml_content = <<XML
    <policies>
    <outbound>
            <base />
            <set-header name="Strict-Transport-Security" exists-action="override">
              <value>max-age=31536000;includeSubDomains; preload</value>
            </set-header>
            <set-header name="X-XSS-Protection" exists-action="override">
              <value>1; mode=block</value>
            </set-header>
            <set-header name="Content-Security-Policy" exists-action="override">
              <value>default-src 'self'</value>
            </set-header>
            <set-header name="X-Frame-Options" exists-action="override">
              <value>deny</value>
            </set-header>
            <set-header name="X-Content-Type-Options" exists-action="override">
              <value>nosniff</value>
            </set-header>
        </outbound>
    </policies>
    XML
}

##########################################################
# OUTPUTS
##########################################################

output "UPADSService_api_resource_group_name" {
  value = data.azurerm_api_management.apim.resource_group_name
}

output "apim_resource_group_name" {
  value = data.azurerm_resource_group.resource_group_apim.name
}

output "apim_resource_group_id" {
  value = data.azurerm_resource_group.resource_group_apim.id
}

output "apim_resource_group_location" {
  value = data.azurerm_resource_group.resource_group_apim.location
}

output "apim_id" {
  value = data.azurerm_api_management.apim.id
}

output "apim_name" {
  value = data.azurerm_api_management.apim.name
}



