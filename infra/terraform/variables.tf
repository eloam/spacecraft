variable "environment_name" {
  description = "Environment this stack deploys to, used to prefix resource and subdomain names"
  type        = string

  validation {
    condition     = contains(["staging", "prod"], var.environment_name)
    error_message = "Allowed values: staging, prod."
  }
}

variable "frontend_name" {
  description = "Subdomain label serving the React app"
  type        = string
  default     = "app"
}

variable "backend_name" {
  description = "Subdomain label serving the API container"
  type        = string
  default     = "api"
}

variable "mail_name" {
  description = "Subdomain label used as the transactional email sending domain"
  type        = string
  default     = "mail"
}

variable "smtp_from_local_part" {
  description = "Local part of the sender address, before the @"
  type        = string
  default     = "noreply"
}

variable "smtp_from_name" {
  description = "Display name shown to recipients"
  type        = string
  default     = "SpaceCraft"
}