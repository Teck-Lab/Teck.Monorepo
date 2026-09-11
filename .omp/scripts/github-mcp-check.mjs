#!/usr/bin/env node

const gatewayUrl = process.env.MCP_GATEWAY_URL?.trim();
if (!gatewayUrl) {
  throw new Error("MCP_GATEWAY_URL is unavailable");
}

const decodeResponse = async (response) => {
  if (!response.ok) {
    throw new Error(`Docker Sandbox MCP gateway returned HTTP ${response.status}`);
  }
  if (response.status === 202 || response.status === 204) return undefined;
  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("text/event-stream")) {
    const payload = (await response.text()).split(/\r?\n/).find((line) => line.startsWith("data:"));
    if (!payload) throw new Error("Docker Sandbox MCP gateway returned no SSE data");
    return JSON.parse(payload.slice(5).trim());
  }
  return await response.json();
};

const post = async (message, sessionId, protocolVersion) => {
  const headers = {
    Accept: "application/json, text/event-stream",
    "Content-Type": "application/json",
  };
  if (sessionId) headers["Mcp-Session-Id"] = sessionId;
  if (protocolVersion) headers["MCP-Protocol-Version"] = protocolVersion;
  const response = await fetch(gatewayUrl, {
    method: "POST",
    headers,
    body: JSON.stringify(message),
    signal: AbortSignal.timeout(30_000),
  });
  return {
    message: await decodeResponse(response),
    sessionId: response.headers.get("Mcp-Session-Id") ?? sessionId,
  };
};

const protocolVersion = "2025-06-18";
const initialized = await post({
  jsonrpc: "2.0",
  id: 1,
  method: "initialize",
  params: {
    protocolVersion,
    capabilities: {},
    clientInfo: { name: "teck-sandbox-readiness", version: "1" },
  },
});
if (initialized.message?.error) {
  throw new Error(initialized.message.error.message ?? "MCP initialize failed");
}
if (!initialized.sessionId) throw new Error("Docker Sandbox MCP gateway returned no session ID");

await post(
  { jsonrpc: "2.0", method: "notifications/initialized" },
  initialized.sessionId,
  protocolVersion,
);
const tools = await post(
  { jsonrpc: "2.0", id: 2, method: "tools/list", params: {} },
  initialized.sessionId,
  protocolVersion,
);
if (tools.message?.error) throw new Error(tools.message.error.message ?? "MCP tools/list failed");
const names = new Set((tools.message?.result?.tools ?? []).map((tool) => tool.name));
const issueRead = [...names].find((name) => name.includes("issue_read"));
if (!issueRead) {
  throw new Error("Docker Sandbox MCP gateway does not expose GitHub issue_read");
}
const issue = await post(
  {
    jsonrpc: "2.0",
    id: 3,
    method: "tools/call",
    params: {
      name: issueRead,
      arguments: {
        owner: "Teck-Lab",
        repo: "Teck.Monorepo",
        issue_number: 689,
        method: "get",
      },
    },
  },
  initialized.sessionId,
  protocolVersion,
);
if (issue.message?.error || issue.message?.result?.isError) {
  throw new Error(
    issue.message?.error?.message ?? "GitHub MCP could not read Teck.Monorepo issue 689",
  );
}
