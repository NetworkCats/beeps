output "ipv4_address" {
  description = "The server's IPv4 address: where the deploy workflow logs in (DEPLOY_HOST), and the Cloudflare A record's target."
  value       = vultr_instance.beeps.main_ip
  sensitive   = true
}
