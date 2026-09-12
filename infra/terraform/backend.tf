terraform {
  backend "s3" {
    # TODO: restreindre la policy "sc-scaleway-iam-policy" au seul bucket
    # quand les resource-level conditions supporteront Object Storage
    # (resource.name == "spacecraft-<ENV_NAME>-tfstate")
    bucket = "spacecraft-${var.environment_name}-tfstate"
    key    = "staging/terraform.tfstate"
    region = "fr-par"
    
    endpoints = { s3: "https://s3.fr-par.scw.cloud" }

    skip_credentials_validation = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_s3_checksum            = true
    use_lockfile                = true
  } 
}
