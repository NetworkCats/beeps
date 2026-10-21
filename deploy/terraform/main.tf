# The production server: one Vultr High Performance AMD instance on Debian 13, set up by deploy/ansible.
# Cloudflare (DNS, Origin CA, R2, Turnstile) is configured by hand (docs/INSTALL.md).

provider "vultr" {
  # The API key comes from VULTR_API_KEY.
  rate_limit  = 700
  retry_limit = 3
}

data "vultr_os" "debian" {
  filter {
    name   = "name"
    values = ["Debian 13 x64 (trixie)"]
  }
}

# Vultr's firewall lets SSH and HTTPS in from anywhere and drops the rest before it reaches the server. The allow list for
# 443 (Cloudflare's addresses only) is UFW's, kept current on the server (roles/firewall), so it is not repeated here.
resource "vultr_firewall_group" "beeps" {
  description = "beeps production"
}

resource "vultr_firewall_rule" "allowed" {
  for_each = {
    ssh   = { protocol = "tcp", port = "22" }
    https = { protocol = "tcp", port = "443" }
    # Path MTU discovery needs "fragmentation needed" through; ping helps when something is down.
    icmp = { protocol = "icmp", port = null }
  }

  firewall_group_id = vultr_firewall_group.beeps.id
  protocol          = each.value.protocol
  ip_type           = "v4"
  subnet            = "0.0.0.0"
  subnet_size       = 0
  port              = each.value.port
  notes             = each.key
}

resource "vultr_instance" "beeps" {
  label    = "beeps-production"
  hostname = var.hostname
  region   = var.region
  plan     = var.plan
  os_id    = data.vultr_os.debian.id
  # IPv4 only: Cloudflare reaches the origin over IPv4, and visitors still get IPv6 from Cloudflare's edge.
  enable_ipv6       = false
  firewall_group_id = vultr_firewall_group.beeps.id
  # No Vultr backups: the nightly pg_dump (deploy/ansible, beeps-backup) backs up the database, to R2 when configured.
  backups          = "disabled"
  activation_email = false
  ddos_protection  = false
  tags             = ["beeps", "production"]

  # Sensitive, so plans print no login keys: the deploy logs of a public repository are public.
  user_data = sensitive(templatefile("${path.module}/cloud-init.yaml.tftpl", {
    ssh_public_keys = compact([for key in split("\n", var.ssh_public_keys) : trimspace(key)])
  }))

  lifecycle {
    # The database lives on this server.
    prevent_destroy = true
    # Only read on the first boot; changing it later must not replace the server. Keys change through Ansible.
    ignore_changes = [user_data]
  }
}
