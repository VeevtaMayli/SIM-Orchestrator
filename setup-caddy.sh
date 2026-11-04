#!/bin/bash
# Caddy installation and setup for Ubuntu 24.04.
# Usage: ./setup-caddy.sh sms.example.com   (or DOMAIN=sms.example.com ./setup-caddy.sh)

set -euo pipefail

DOMAIN="${1:-${DOMAIN:-}}"
if [[ -z "$DOMAIN" ]]; then
    echo "Usage: $0 <domain>" >&2
    echo "The domain must already point to this server (A/AAAA record) so Let's Encrypt can issue a certificate." >&2
    exit 1
fi

echo "📦 Installing Caddy..."

sudo apt update
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install -y caddy

echo "✅ Caddy installed successfully"

echo "📝 Creating Caddyfile for ${DOMAIN}..."
sudo tee /etc/caddy/Caddyfile > /dev/null <<EOF
${DOMAIN} {
    # Automatic HTTPS with Let's Encrypt

    # Reverse proxy to the Docker container (Caddy adds X-Forwarded-For/-Proto)
    reverse_proxy localhost:5000

    # Security headers
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"
        X-Frame-Options "DENY"
        X-Content-Type-Options "nosniff"
        Referrer-Policy "no-referrer"
    }

    # Logging
    log {
        output file /var/log/caddy/sim-orchestrator.log
        format json
    }
}
EOF

sudo caddy validate --config /etc/caddy/Caddyfile
echo "✅ Caddyfile created"

sudo mkdir -p /var/log/caddy
sudo chown caddy:caddy /var/log/caddy

echo "🔥 Configuring firewall..."
sudo ufw allow 80/tcp comment 'HTTP for Lets Encrypt'
sudo ufw allow 443/tcp comment 'HTTPS'
sudo ufw status

echo "🚀 Starting Caddy..."
sudo systemctl enable caddy
sudo systemctl restart caddy
sudo systemctl --no-pager status caddy

echo "✅ Caddy is running!"
echo ""
echo "📋 Next steps:"
echo "1. Wait 30-60 seconds for Let's Encrypt certificate provisioning"
echo "2. Check Caddy logs: sudo journalctl -u caddy -f"
echo "3. Test HTTPS: curl -I https://${DOMAIN}/health"
