resource "scaleway_key_manager_key" "registry_credentials" {
  name        = "spacecraft-${var.environment_name}-registry-credentials"
  description = "Encrypts the npm, NuGet and PyPI credentials users submit to publish their SDKs"
  project_id  = data.scaleway_account_project.env.id
  region      = "fr-par"

  usage     = "symmetric_encryption"
  algorithm = "aes_256_gcm"

  rotation_policy {
    rotation_period = "8760h" # 1 year
  }
}

# IAM

resource "scaleway_iam_policy" "backend_kms" {
  name           = "sc-${var.environment_name}-backend-kms-policy"
  description    = "Encrypt registry credentials submitted by users"
  application_id = scaleway_iam_application.backend.id

  rule {
    project_ids          = [data.scaleway_account_project.env.id]
    permission_set_names = ["KeyManagerDataEncrypt"]
  }
}

resource "scaleway_iam_policy" "sdk_runner_kms" {
  name           = "sc-${var.environment_name}-sdk-runner-kms-policy"
  description    = "Decrypt registry credentials to publish generated SDKs"
  application_id = scaleway_iam_application.sdk_runner.id

  rule {
    project_ids          = [data.scaleway_account_project.env.id]
    permission_set_names = ["KeyManagerDataDecrypt"]
  }
}