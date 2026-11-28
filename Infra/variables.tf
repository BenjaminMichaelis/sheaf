variable "CLIENT_ID" {
  description = "Value of the client id of the service principal"
  type        = string
  default     = ""
}

variable "TENANT_ID" {
  type        = string
  description = "Value of the tenant id of the service principal"
  default     = ""
}

variable "planning_center" {
  description = "Planning Center OAuth app settings for production, or null to leave the integration unconfigured. See README, Planning Center connection."
  type = object({
    client_id                         = string
    redirect_uri                      = string
    return_url                        = string
    user_agent                        = string
    client_secret_key_vault_secret_id = string
  })
  default = null
}

variable "SUBSCRIPTION_ID" {
  type        = string
  description = "Value of the subscription id to use"
  default     = ""
}
