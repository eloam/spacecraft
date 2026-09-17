resource "scaleway_registry_namespace" "main" {
  name        = "spacecraft-${var.environment_name}"
  description = "Container images for the ${var.environment_name} environment"
  is_public   = false
}