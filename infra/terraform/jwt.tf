resource "random_password" "jwt_signing_key" {
  length  = 64
  special = false
}

resource "scaleway_secret" "jwt" {
  name       = "jwt-signing-key"
  path       = "/${var.environment_name}"
  project_id = data.scaleway_account_project.env.id
}