#!/bin/bash
# Caddy Installation and Setup Script for Ubuntu 24.04

set -e  # Exit on error

echo "📦 Installing Caddy..."

# Install Caddy
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install -y caddy

echo "✅ Caddy installed successfully"

# Create Caddyfile
echo "📝 Creating Caddyfile..."
sudo tee /etc/caddy/Caddyfile > /dev/null <<'EOF'
%YOUR_DOMAIN% {
    # Automatic HTTPS with Let's Encrypt

    # Reverse proxy to Docker container
    reverse_proxy localhost:5000

    # Security headers
    header {
        # Enable HSTS
        Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"
        # Prevent clickjacking
        X-Frame-Options "DENY"
        # Prevent MIME sniffing
        X-Content-Type-Options "nosniff"
        # Enable XSS protection
        X-XSS-Protection "1; mode=block"
    }

    # Logging
    log {
        output file /var/log/caddy/sim-orchestrator.log
        format json
    }
}
EOF

echo "✅ Caddyfile created"

# Create log directory
sudo mkdir -p /var/log/caddy
sudo chown caddy:caddy /var/log/caddy

# Open firewall ports
echo "🔥 Configuring firewall..."
sudo ufw allow 80/tcp comment 'HTTP for Lets Encrypt'
sudo ufw allow 443/tcp comment 'HTTPS'
sudo ufw status

echo "✅ Firewall configured"

# Enable and start Caddy
echo "🚀 Starting Caddy..."
sudo systemctl enable caddy
sudo systemctl restart caddy
sudo systemctl status caddy

echo "✅ Caddy is running!"
echo ""
echo "📋 Next steps:"
echo "1. Wait 30-60 seconds for Let's Encrypt certificate provisioning"
echo "2. Check Caddy logs: sudo journalctl -u caddy -f"
echo "3. Test HTTPS: curl -I https://%YOUR_DOMAIN%/health"
