resource "scaleway_registry_namespace" "main" {
  name        = "spacecraft-registry"
  description = "Main container registry"
  is_public   = false
}