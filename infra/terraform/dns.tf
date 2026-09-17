# Domains and subdomains
resource "cloudflare_record" "frontend" {
  zone_id = var.cloudflare_zone_id
  name    = var.frontend_name
  content = scaleway_object_bucket_website_configuration.frontend.website_endpoint
  type    = "CNAME"
  proxied = true
}

resource "cloudflare_record" "backend" {
  zone_id = var.cloudflare_zone_id
  name    = var.backend_name
  content = scaleway_container.main.domain_name
  type    = "CNAME"
  proxied = true
}

# Mails
resource "cloudflare_record" "mail_spf" {
  zone_id = var.cloudflare_zone_id
  name    = local.mail_subdomain
  type    = "TXT"
  content = scaleway_tem_domain.main.spf_config
  proxied = false
}

resource "cloudflare_record" "mail_dkim" {
  zone_id = var.cloudflare_zone_id
  name    = scaleway_tem_domain.main.dkim_name
  type    = "TXT"
  content = scaleway_tem_domain.main.dkim_config
  proxied = false
}

resource "cloudflare_record" "mail_mx" {
  zone_id  = var.cloudflare_zone_id
  name     = local.mail_subdomain
  type     = "MX"
  content  = scaleway_tem_domain.main.mx_config
  priority = 10
  proxied  = false
}

resource "cloudflare_record" "mail_dmarc" {
  zone_id = var.cloudflare_zone_id
  name    = scaleway_tem_domain.main.dmarc_name
  type    = "TXT"
  content = scaleway_tem_domain.main.dmarc_config
  proxied = false
}