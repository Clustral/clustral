const controlPlaneUrl =
  process.env.CONTROLPLANE_URL || "http://localhost:5000";

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "standalone",

  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${controlPlaneUrl}/api/:path*`,
      },
      {
        source: "/.well-known/clustral-configuration",
        destination: `${controlPlaneUrl}/api/v1/config`,
      },
    ];
  },
};

export default nextConfig;
