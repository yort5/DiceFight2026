import { createContext, useContext, useEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { splitKeywordText } from "./keywordRules";
import "./KeywordPreview.css";

interface Preview { title: string; explanation: string; opener: HTMLButtonElement; mobile: boolean; accent: string }
const PreviewContext = createContext<(preview: Preview) => void>(() => {});

export function KeywordPreviewProvider({ children }: { children: ReactNode }) {
  const [preview, setPreview] = useState<Preview | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    if (!preview) return;
    const dialog = dialogRef.current;
    dialog?.showModal();
    dialog?.focus();
    return () => {
      dialog?.close();
      if (preview.opener.isConnected) preview.opener.focus({ preventScroll: true });
    };
  }, [preview]);

  return <PreviewContext value={setPreview}>
    {children}
    {preview && <dialog
      ref={dialogRef}
      className={`keyword-preview dicekingdom${preview.mobile ? " dk-mobile" : ""}`}
      style={{ "--cc": preview.accent || "var(--dkm-border)" } as CSSProperties}
      aria-labelledby="keyword-preview-title"
      aria-describedby="keyword-preview-explanation"
      aria-modal="true"
      tabIndex={-1}
      onPointerDown={event => event.stopPropagation()}
      onKeyDown={event => event.stopPropagation()}
      onCancel={event => { event.preventDefault(); setPreview(null); }}
      onClick={event => { event.stopPropagation(); setPreview(null); }}
    >
      <article className={`keyword-preview-card card-popover${preview.mobile ? " dkm-inspect" : ""}`}>
        <div className="card-popover-head">
          <h2 id="keyword-preview-title" className={preview.mobile ? "dkm-inspect-name" : "card-popover-name"}>{preview.title}</h2>
        </div>
        <p id="keyword-preview-explanation" className="card-popover-text">{preview.explanation}</p>
      </article>
    </dialog>}
  </PreviewContext>;
}

/** Inline, so it can be used in existing paragraphs without changing their layout. */
export function KeywordText({ text }: { text: string }) {
  const openPreview = useContext(PreviewContext);
  return <>{splitKeywordText(text).map((part, index) => part.keyword ? (
    <button
      key={index}
      type="button"
      className="rules-keyword"
      aria-haspopup="dialog"
      aria-label={`Explain ${part.text}`}
      title={`Tap for ${part.keyword} rules`}
      onPointerDown={event => event.stopPropagation()}
      onKeyDown={event => event.stopPropagation()}
      onClick={event => {
        event.stopPropagation();
        openPreview({
          title: part.text, explanation: part.explanation!, opener: event.currentTarget,
          mobile: Boolean(event.currentTarget.closest(".dk-mobile")),
          accent: getComputedStyle(event.currentTarget).getPropertyValue("--cc").trim(),
        });
      }}
    >{part.text}</button>
  ) : part.text)}</>;
}

// Keep the existing whole-card action, with separate keyword buttons above it.
// In particular, disabled purchase actions must not disable rules previews.
export function KeywordAction({ children, className, label, title, disabled, onClick }: {
  children: ReactNode; className: string; label: string; title?: string;
  disabled?: boolean; onClick: () => void;
}) {
  return <div className={`${className} keyword-action-wrap`} title={title}>
    <button type="button" className="keyword-action-target" aria-label={label} disabled={disabled} onClick={onClick} />
    {children}
  </div>;
}
