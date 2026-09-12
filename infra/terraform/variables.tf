variable "environment_name" {
  description = "Environment this stack deploys to"
  type        = string

  validation {
    condition     = contains(["staging", "prod"], var.environment_name)
    error_message = "Allowed values: staging, prod."
  }
}