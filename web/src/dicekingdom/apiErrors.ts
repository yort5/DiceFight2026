/** Show ASP.NET validation responses instead of an unhelpful "Request failed: 400". */
export function apiErrorMessage(status: number, response: unknown): string {
  if (!response || typeof response !== "object") return `Request failed: ${status}`;
  const body = response as Record<string, unknown>;
  const errors = body.errors && typeof body.errors === "object"
    ? Object.entries(body.errors as Record<string, unknown>).flatMap(([field, value]) => {
      const messages = Array.isArray(value) ? value.filter((v): v is string => typeof v === "string") : [];
      return messages.map((message) => `${field}: ${message}`);
    })
    : [];

  // An older V2 API requires both Champions. The custom-team UI deliberately
  // sends no Player 1 Champion, so ASP.NET rejects it before Create() executes.
  if (status === 400 && errors.some((message) => /PlayerOneChampionId/i.test(message))) {
    return "The running game server does not appear to support custom teams yet. Rebuild and restart (or redeploy) the updated DiceFight.Api backend, then try again. Server validation: " + errors.join("; ");
  }
  if (typeof body.error === "string" && body.error) return body.error;
  if (errors.length > 0) return `Request failed: ${status} — ${errors.join("; ")}`;
  if (typeof body.detail === "string" && body.detail) return body.detail;
  if (typeof body.title === "string" && body.title) return `Request failed: ${status} — ${body.title}`;
  return `Request failed: ${status}`;
}
