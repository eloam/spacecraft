locals {
  # domain
  root_domain        = "spacecraft.sh"
  cloudflare_zone_id = "023e105f4ecef8ad9ca31a8372d0c353"
  env_prefix         = var.environment_name == "prod" ? "" : "${var.environment_name}-"

  frontend_domain = "${local.env_prefix}${var.frontend_name}.${local.root_domain}"
  backend_domain  = "${local.env_prefix}${var.backend_name}.${local.root_domain}"
  mail_subdomain  = "${local.env_prefix}${var.mail_name}"          # e.g. staging-mail
  mail_domain     = "${local.mail_subdomain}.${local.root_domain}" # e.g. staging-mail.spacecraft.sh

  frontend_url = "https://${local.frontend_domain}"
  backend_url  = "https://${local.backend_domain}"

  # transactional email
  smtp_host       = "smtp.tem.scaleway.com"
  smtp_port       = 465
  smtp_security   = "ImplicitTls"
  smtp_from_email = "${var.smtp_from_local_part}@${local.mail_domain}"
}