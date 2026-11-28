variable "acr_login_server" {
  description = "The login server of the container registry."
  type        = string
}

variable "environment" {
  description = "The deployment environment (e.g., Dev, Prod)"
  type        = string
}

variable "location" {
  description = "Azure region for the resources"
  type        = string
}

variable "planning_center" {
  description = "Planning Center OAuth app settings, or null to leave the integration unconfigured. The client secret is a versionless Key Vault secret ID, never the secret itself."
  type = object({
    client_id                         = string
    redirect_uri                      = string
    return_url                        = string
    user_agent                        = string
    client_secret_key_vault_secret_id = string
  })
  default = null
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
