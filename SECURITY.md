# Reporting a security issue

Do not attach production logs, account lists, network configurations, access tokens or credentials to public issues. For a suspected vulnerability, contact the repository owner privately through an available GitHub contact method. Provide a minimal reproduction using synthetic data.

The TCP listener has no TLS or sender authentication. Deploy it on a trusted network with explicit source restrictions and firewall rules. Source IP restrictions do not replace authentication.
