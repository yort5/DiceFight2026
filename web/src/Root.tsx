import App from "./App";
import { DiceKingdomPage } from "./dicekingdom/DiceKingdomPage";
import { DiceKingdomMobilePage } from "./dicekingdom/DiceKingdomMobilePage";
import { useRoute } from "./router";
import { TeamBuilderPage } from "./TeamBuilderPage";
import { KeywordPreviewProvider } from "./KeywordPreview";
import { useTheme } from "./theme";

export function Root() {
  useTheme();
  const route = useRoute();
  return <KeywordPreviewProvider key={route}><Screen route={route} /></KeywordPreviewProvider>;
}

function Screen({ route }: { route: string }) {
  if (route === "/teambuilder") return <TeamBuilderPage />;
  if (route === "/dice-kingdom/mobile") return <DiceKingdomMobilePage />;
  if (route === "/dice-kingdom") return <DiceKingdomPage />;
  return <App />;
}
