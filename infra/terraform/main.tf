data "scaleway_account_project" "env" {
}

output "debug" {
  value = var.environment_name
}