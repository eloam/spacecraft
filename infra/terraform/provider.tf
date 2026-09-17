terraform {
  required_providers {
    scaleway = {
      source = "scaleway/scaleway"
    }
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5"
    }
  }
  required_version = ">= 0.13"
}