//@ts-check

const releaseVersion = process.env.TECK_RELEASE_VERSION ?? require("./package.json").version;
const sourceRevision = process.env.TECK_SOURCE_REVISION ?? "development";

/** @type {import('next').NextConfig} */
const nextConfig = {
  env: {
    NEXT_PUBLIC_TECK_RELEASE_VERSION: releaseVersion,
    NEXT_PUBLIC_TECK_SOURCE_REVISION: sourceRevision,
  },
  generateBuildId: async () => `${releaseVersion}-${sourceRevision.slice(0, 12)}`,
};

module.exports = nextConfig;
