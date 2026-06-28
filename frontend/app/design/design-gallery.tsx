"use client";

import { useState } from "react";
import { toast } from "sonner";

// ── UI primitives ──────────────────────────────────────────────────────────────
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter, CardAction } from "@/components/ui/card";
import { Alert, AlertTitle, AlertDescription } from "@/components/ui/alert";
import { Avatar, AvatarFallback, AvatarImage, AvatarGroup, AvatarGroupCount, AvatarBadge } from "@/components/ui/avatar";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { Progress } from "@/components/ui/progress";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Tooltip, TooltipTrigger, TooltipContent } from "@/components/ui/tooltip";
import {
  Dialog,
  DialogTrigger,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Sheet,
  SheetTrigger,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetFooter,
} from "@/components/ui/sheet";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
  PopoverHeader,
  PopoverTitle,
  PopoverDescription,
} from "@/components/ui/popover";

// ── App components ─────────────────────────────────────────────────────────────
import { EmptyState } from "@/components/app/empty-state";
import { MoneyAmount } from "@/components/app/money-amount";
import { DataTable, type ColumnDef } from "@/components/app/data-table";
import { ProblemDetails } from "@/components/app/problem-details";
import { ApiError } from "@/lib/api/types";

import {
  InfoIcon,
  TriangleAlertIcon,
  OctagonXIcon,
  InboxIcon,
  PlusIcon,
  MoreHorizontalIcon,
  UserIcon,
  SettingsIcon,
  LogOutIcon,
  CheckCircle2Icon,
  XCircleIcon,
  LoaderCircleIcon,
} from "lucide-react";

// ─────────────────────────────────────────────────────────────────────────────
// Sample data for DataTable
// ─────────────────────────────────────────────────────────────────────────────

interface SampleRow {
  id: string;
  name: string;
  status: "active" | "inactive" | "pending";
  credits: number;
  joined: string;
}

const SAMPLE_ROWS: SampleRow[] = [
  { id: "1", name: "Alice Johnson", status: "active", credits: 1250, joined: "2024-01-12" },
  { id: "2", name: "Bob Smith", status: "inactive", credits: 320, joined: "2024-03-07" },
  { id: "3", name: "Carol White", status: "pending", credits: 0, joined: "2024-06-01" },
  { id: "4", name: "David Lee", status: "active", credits: 8400, joined: "2023-11-20" },
  { id: "5", name: "Eva Müller", status: "active", credits: 2100, joined: "2024-02-14" },
];

const TABLE_COLUMNS: ColumnDef<SampleRow>[] = [
  {
    key: "name",
    header: "Name",
    cell: (row) => (
      <span className="font-medium">{row.name}</span>
    ),
  },
  {
    key: "status",
    header: "Status",
    cell: (row) => (
      <Badge
        variant={
          row.status === "active"
            ? "default"
            : row.status === "pending"
              ? "secondary"
              : "outline"
        }
      >
        {row.status}
      </Badge>
    ),
  },
  {
    key: "credits",
    header: "Credits",
    className: "text-right",
    cell: (row) => <MoneyAmount value={row.credits} />,
  },
  {
    key: "joined",
    header: "Joined",
    cell: (row) => (
      <span className="text-muted-foreground text-xs tabular-nums">{row.joined}</span>
    ),
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Section wrapper
// ─────────────────────────────────────────────────────────────────────────────

function Section({
  id,
  title,
  children,
}: {
  id: string;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section id={id} className="space-y-4 scroll-mt-16">
      <div className="flex items-center gap-3">
        <h2 className="text-base font-semibold tracking-tight">{title}</h2>
        <div className="flex-1 h-px bg-border" />
      </div>
      {children}
    </section>
  );
}

function SubSection({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2">
      <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">{label}</p>
      <div className="flex flex-wrap gap-2 items-start">{children}</div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Color swatch
// ─────────────────────────────────────────────────────────────────────────────

function ColorSwatch({ label, bg, text }: { label: string; bg: string; text?: string }) {
  return (
    <div className="flex flex-col gap-1 items-center">
      <div
        className={`h-10 w-16 rounded-md ring-1 ring-border ${bg} ${text ?? ""}`}
        aria-label={label}
      />
      <span className="text-xs text-muted-foreground text-center leading-tight">{label}</span>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Fake OperationStatus panels (no real polling needed for the gallery)
// ─────────────────────────────────────────────────────────────────────────────

function FakeOperationStatus({
  status,
  errorDetail,
}: {
  status: "Pending" | "Running" | "Succeeded" | "Failed";
  errorDetail?: string;
}) {
  const terminal = status === "Succeeded" || status === "Failed";
  return (
    <div className="flex flex-col gap-2" aria-label={`Operation: ${status}`}>
      <div className="flex items-center gap-2 text-sm">
        {terminal ? (
          status === "Succeeded" ? (
            <CheckCircle2Icon className="h-4 w-4 text-success" aria-hidden />
          ) : (
            <XCircleIcon className="h-4 w-4 text-destructive" aria-hidden />
          )
        ) : (
          <LoaderCircleIcon className="h-4 w-4 animate-spin text-muted-foreground" aria-hidden />
        )}
        <span
          className={
            status === "Succeeded"
              ? "font-medium text-success"
              : status === "Failed"
                ? "font-medium text-destructive"
                : "font-medium text-muted-foreground"
          }
        >
          {status}
        </span>
      </div>
      {!terminal && (
        <Progress value={null} className="h-1 animate-pulse" aria-label="In progress" />
      )}
      {status === "Failed" && errorDetail && (
        <p className="text-xs text-destructive">{errorDetail}</p>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Fake RealtimeIndicator panels
// ─────────────────────────────────────────────────────────────────────────────

function FakeRealtimeIndicator({ status }: { status: "open" | "connecting" | "closed" }) {
  const label = { open: "Live", connecting: "Connecting", closed: "Offline" }[status];
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
      <span
        aria-hidden="true"
        className={[
          "h-1.5 w-1.5 rounded-full",
          status === "open" ? "bg-success" : "",
          status === "connecting" ? "animate-pulse bg-warning" : "",
          status === "closed" ? "bg-muted-foreground/50" : "",
        ].join(" ")}
      />
      {label}
    </span>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Main gallery
// ─────────────────────────────────────────────────────────────────────────────

export function DesignGallery() {
  const [switchOn, setSwitchOn] = useState(false);
  const [checked, setChecked] = useState(false);
  const [tablePage, setTablePage] = useState(1);
  const [progress40] = useState(40);

  return (
    <div className="min-h-screen bg-background text-foreground">
      {/* ── Sticky header ── */}
      <header className="sticky top-0 z-20 flex items-center gap-3 border-b border-border bg-background px-6 py-3">
        <h1 className="text-sm font-semibold tracking-tight">Design Gallery</h1>
        <Badge variant="secondary">dev only</Badge>
        <span className="ml-auto text-xs text-muted-foreground">
          Use the ThemeToggle (top-right in the shell) to preview dark mode.
        </span>
      </header>

      <div className="mx-auto max-w-5xl space-y-16 px-6 py-10">

        {/* ════════════════════════════════════════════════════════════════
            1. DESIGN TOKENS
        ════════════════════════════════════════════════════════════════ */}
        <Section id="tokens" title="1 · Design Tokens">

          {/* Colors */}
          <SubSection label="Semantic color pairs (OKLCH)">
            <ColorSwatch label="background" bg="bg-background" />
            <ColorSwatch label="foreground" bg="bg-foreground" />
            <ColorSwatch label="card" bg="bg-card" />
            <ColorSwatch label="primary" bg="bg-primary" />
            <ColorSwatch label="primary-fg" bg="bg-primary-foreground" />
            <ColorSwatch label="secondary" bg="bg-secondary" />
            <ColorSwatch label="muted" bg="bg-muted" />
            <ColorSwatch label="muted-fg" bg="bg-muted-foreground" />
            <ColorSwatch label="accent" bg="bg-accent" />
            <ColorSwatch label="destructive" bg="bg-destructive" />
            <ColorSwatch label="border" bg="bg-border" />
            <ColorSwatch label="input" bg="bg-input" />
            <ColorSwatch label="ring" bg="bg-ring" />
          </SubSection>

          {/* Typography scale */}
          <SubSection label="Typography scale">
            <div className="w-full space-y-1">
              <p className="text-xs text-muted-foreground">text-xs (12 px) — caption, metadata</p>
              <p className="text-sm text-muted-foreground">text-sm (14 px) — body default</p>
              <p className="text-base">text-base (16 px) — input, body large</p>
              <p className="text-lg font-medium">text-lg font-medium — sub-heading</p>
              <p className="text-xl font-semibold tracking-tight">text-xl font-semibold — page title</p>
              <p className="text-2xl font-bold tracking-tight">text-2xl font-bold — hero</p>
              <p className="text-sm tabular-nums font-medium text-right w-32">1,234,567 — tabular-nums</p>
            </div>
          </SubSection>

          {/* Spacing scale */}
          <SubSection label="Spacing scale (4-px grid)">
            {[1, 2, 3, 4, 6, 8, 10, 12, 16].map((n) => (
              <div key={n} className="flex flex-col items-center gap-1">
                <div
                  className="bg-primary rounded"
                  style={{ width: `${n * 4}px`, height: "12px" }}
                  aria-label={`${n * 4}px`}
                />
                <span className="text-xs text-muted-foreground">{n * 4}px</span>
              </div>
            ))}
          </SubSection>

          {/* Radius scale */}
          <SubSection label="Border radius (--radius = 0.625rem)">
            {(["rounded-sm", "rounded-md", "rounded-lg", "rounded-xl", "rounded-2xl", "rounded-full"] as const).map(
              (cls) => (
                <div key={cls} className="flex flex-col items-center gap-1">
                  <div className={`h-8 w-16 bg-muted border border-border ${cls}`} />
                  <span className="text-xs text-muted-foreground">{cls}</span>
                </div>
              ),
            )}
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            2. BUTTON
        ════════════════════════════════════════════════════════════════ */}
        <Section id="buttons" title="2 · Button">
          <SubSection label="Variants">
            <Button variant="default">Default</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="outline">Outline</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="destructive">Destructive</Button>
            <Button variant="link">Link</Button>
          </SubSection>

          <SubSection label="Sizes">
            <Button size="xs">XS</Button>
            <Button size="sm">SM</Button>
            <Button size="default">Default</Button>
            <Button size="lg">LG</Button>
            <Button size="icon" aria-label="icon"><PlusIcon /></Button>
            <Button size="icon-sm" aria-label="icon-sm"><PlusIcon /></Button>
            <Button size="icon-xs" aria-label="icon-xs"><PlusIcon /></Button>
          </SubSection>

          <SubSection label="States">
            <Button disabled>Disabled</Button>
            <Button variant="outline" disabled>Disabled outline</Button>
            <Button>
              <LoaderCircleIcon className="animate-spin" />
              Loading…
            </Button>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            3. BADGE
        ════════════════════════════════════════════════════════════════ */}
        <Section id="badges" title="3 · Badge">
          <SubSection label="Variants">
            <Badge variant="default">Default</Badge>
            <Badge variant="secondary">Secondary</Badge>
            <Badge variant="outline">Outline</Badge>
            <Badge variant="destructive">Destructive</Badge>
            <Badge variant="ghost">Ghost</Badge>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            4. AVATAR
        ════════════════════════════════════════════════════════════════ */}
        <Section id="avatars" title="4 · Avatar">
          <SubSection label="Sizes + fallback">
            <Avatar size="sm"><AvatarFallback>AB</AvatarFallback></Avatar>
            <Avatar size="default"><AvatarFallback>CD</AvatarFallback></Avatar>
            <Avatar size="lg"><AvatarFallback>EF</AvatarFallback></Avatar>
            <Avatar size="default">
              <AvatarImage src="https://i.pravatar.cc/80?img=5" alt="User" />
              <AvatarFallback>GH</AvatarFallback>
            </Avatar>
          </SubSection>
          <SubSection label="With badge">
            <Avatar size="default">
              <AvatarFallback>MP</AvatarFallback>
              <AvatarBadge />
            </Avatar>
          </SubSection>
          <SubSection label="Group">
            <AvatarGroup>
              <Avatar size="sm"><AvatarFallback>A</AvatarFallback></Avatar>
              <Avatar size="sm"><AvatarFallback>B</AvatarFallback></Avatar>
              <Avatar size="sm"><AvatarFallback>C</AvatarFallback></Avatar>
              <AvatarGroupCount>+4</AvatarGroupCount>
            </AvatarGroup>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            5. FORM INPUTS
        ════════════════════════════════════════════════════════════════ */}
        <Section id="inputs" title="5 · Form Inputs">
          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="gi-default">Input — default</Label>
              <Input id="gi-default" placeholder="Type something…" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="gi-disabled">Input — disabled</Label>
              <Input id="gi-disabled" placeholder="Disabled" disabled value="Can't touch this" readOnly />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="gi-invalid">Input — invalid</Label>
              <Input id="gi-invalid" aria-invalid="true" defaultValue="bad@" />
              <p className="text-xs text-destructive">Enter a valid email address.</p>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="gi-password">Input — password</Label>
              <Input id="gi-password" type="password" defaultValue="secret" />
            </div>
            <div className="space-y-1.5 sm:col-span-2">
              <Label htmlFor="gi-textarea">Textarea — default</Label>
              <Textarea id="gi-textarea" placeholder="Multiline input…" rows={3} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="gi-textarea-disabled">Textarea — disabled</Label>
              <Textarea id="gi-textarea-disabled" placeholder="Disabled" disabled />
            </div>
          </div>

          <SubSection label="Select">
            <Select>
              <SelectTrigger className="w-48">
                <SelectValue placeholder="Select a status" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="active">Active</SelectItem>
                <SelectItem value="inactive">Inactive</SelectItem>
                <SelectItem value="pending">Pending</SelectItem>
              </SelectContent>
            </Select>
            <Select disabled>
              <SelectTrigger className="w-40">
                <SelectValue placeholder="Disabled" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="x">Option</SelectItem>
              </SelectContent>
            </Select>
          </SubSection>

          <SubSection label="Checkbox">
            <div className="flex items-center gap-2">
              <Checkbox
                id="cb-unchecked"
                checked={false}
                onCheckedChange={() => {}}
              />
              <Label htmlFor="cb-unchecked">Unchecked</Label>
            </div>
            <div className="flex items-center gap-2">
              <Checkbox
                id="cb-checked"
                checked={checked}
                onCheckedChange={(v) => setChecked(Boolean(v))}
              />
              <Label htmlFor="cb-checked">Controlled ({checked ? "on" : "off"})</Label>
            </div>
            <div className="flex items-center gap-2">
              <Checkbox id="cb-disabled" disabled />
              <Label htmlFor="cb-disabled" className="opacity-50">Disabled</Label>
            </div>
          </SubSection>

          <SubSection label="Switch">
            <div className="flex items-center gap-2">
              <Switch
                id="sw-controlled"
                checked={switchOn}
                onCheckedChange={setSwitchOn}
              />
              <Label htmlFor="sw-controlled">{switchOn ? "On" : "Off"}</Label>
            </div>
            <div className="flex items-center gap-2">
              <Switch id="sw-disabled" disabled />
              <Label htmlFor="sw-disabled" className="opacity-50">Disabled</Label>
            </div>
            <div className="flex items-center gap-2">
              <Switch size="sm" id="sw-sm" defaultChecked />
              <Label htmlFor="sw-sm">Small</Label>
            </div>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            6. CARD
        ════════════════════════════════════════════════════════════════ */}
        <Section id="cards" title="6 · Card">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Card>
              <CardHeader>
                <CardTitle>Default card</CardTitle>
                <CardDescription>With header + content + footer.</CardDescription>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-muted-foreground">
                  Content area for any child components.
                </p>
              </CardContent>
              <CardFooter>
                <Button size="sm" variant="outline">Cancel</Button>
                <Button size="sm" className="ml-auto">Save</Button>
              </CardFooter>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>With action slot</CardTitle>
                <CardAction>
                  <Button size="icon-sm" variant="ghost" aria-label="More"><MoreHorizontalIcon /></Button>
                </CardAction>
                <CardDescription>CardAction sits in the header grid.</CardDescription>
              </CardHeader>
              <CardContent>
                <MoneyAmount value={4250} />
                <span className="ml-1 text-xs text-muted-foreground">credits available</span>
              </CardContent>
            </Card>

            <Card size="sm">
              <CardHeader>
                <CardTitle>Small card (size=sm)</CardTitle>
                <CardDescription>Tighter spacing variant.</CardDescription>
              </CardHeader>
              <CardContent>
                <div className="flex flex-wrap gap-1">
                  <Badge variant="default">active</Badge>
                  <Badge variant="secondary">pending</Badge>
                  <Badge variant="outline">draft</Badge>
                </div>
              </CardContent>
            </Card>
          </div>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            7. ALERT
        ════════════════════════════════════════════════════════════════ */}
        <Section id="alerts" title="7 · Alert">
          <div className="space-y-3">
            <Alert>
              <InfoIcon />
              <AlertTitle>Info</AlertTitle>
              <AlertDescription>
                This is a default (informational) alert. Great for non-critical notices.
              </AlertDescription>
            </Alert>
            <Alert variant="destructive">
              <OctagonXIcon />
              <AlertTitle>Error</AlertTitle>
              <AlertDescription>
                Something went wrong. Please try again or contact support.
              </AlertDescription>
            </Alert>
            <Alert>
              <TriangleAlertIcon />
              <AlertTitle>Warning (default variant)</AlertTitle>
              <AlertDescription>
                This action cannot be undone. Proceed with caution.
              </AlertDescription>
            </Alert>
          </div>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            8. TABS
        ════════════════════════════════════════════════════════════════ */}
        <Section id="tabs" title="8 · Tabs">
          <SubSection label="Default (pill) variant">
            <Tabs defaultValue="overview" className="w-full">
              <TabsList>
                <TabsTrigger value="overview">Overview</TabsTrigger>
                <TabsTrigger value="activity">Activity</TabsTrigger>
                <TabsTrigger value="settings">Settings</TabsTrigger>
                <TabsTrigger value="disabled" disabled>Disabled</TabsTrigger>
              </TabsList>
              <TabsContent value="overview" className="pt-4">
                <p className="text-sm text-muted-foreground">Overview content panel.</p>
              </TabsContent>
              <TabsContent value="activity" className="pt-4">
                <p className="text-sm text-muted-foreground">Activity feed goes here.</p>
              </TabsContent>
              <TabsContent value="settings" className="pt-4">
                <p className="text-sm text-muted-foreground">Settings panel content.</p>
              </TabsContent>
            </Tabs>
          </SubSection>
          <SubSection label="Line variant">
            <Tabs defaultValue="a">
              <TabsList variant="line">
                <TabsTrigger value="a">Alpha</TabsTrigger>
                <TabsTrigger value="b">Beta</TabsTrigger>
                <TabsTrigger value="c">Gamma</TabsTrigger>
              </TabsList>
            </Tabs>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            9. PROGRESS + SKELETON
        ════════════════════════════════════════════════════════════════ */}
        <Section id="progress" title="9 · Progress &amp; Skeleton">
          <SubSection label="Progress — determinate">
            <div className="w-full max-w-xs space-y-2">
              <Progress value={progress40} />
              <Progress value={75} />
              <Progress value={100} />
            </div>
          </SubSection>
          <SubSection label="Progress — indeterminate (animate-pulse)">
            <div className="w-full max-w-xs">
              <Progress value={null} className="animate-pulse" />
            </div>
          </SubSection>
          <SubSection label="Skeleton — text + avatar">
            <div className="flex items-center gap-3 w-72">
              <Skeleton className="h-8 w-8 rounded-full" />
              <div className="space-y-1.5 flex-1">
                <Skeleton className="h-3.5 w-3/4 rounded" />
                <Skeleton className="h-3 w-1/2 rounded" />
              </div>
            </div>
          </SubSection>
          <SubSection label="Skeleton — table rows">
            <div className="w-full max-w-lg space-y-2">
              {[80, 65, 90, 55].map((w, i) => (
                <div key={i} className="flex gap-4">
                  <Skeleton className="h-4 rounded" style={{ width: `${w}%` }} />
                </div>
              ))}
            </div>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            10. TOOLTIP
        ════════════════════════════════════════════════════════════════ */}
        <Section id="tooltips" title="10 · Tooltip">
          <SubSection label="Sides">
            <Tooltip>
              <TooltipTrigger render={<Button variant="outline" size="sm" />}>
                Top (default)
              </TooltipTrigger>
              <TooltipContent>Tooltip on top</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger render={<Button variant="outline" size="sm" />}>
                Bottom
              </TooltipTrigger>
              <TooltipContent side="bottom">Tooltip on bottom</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger render={<Button variant="outline" size="sm" />}>
                Right
              </TooltipTrigger>
              <TooltipContent side="right">Tooltip on right</TooltipContent>
            </Tooltip>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            11. DROPDOWN MENU
        ════════════════════════════════════════════════════════════════ */}
        <Section id="dropdown" title="11 · Dropdown Menu">
          <SubSection label="Default">
            <DropdownMenu>
              <DropdownMenuTrigger render={<Button variant="outline" size="sm" />}>
                <MoreHorizontalIcon className="mr-1" /> Actions
              </DropdownMenuTrigger>
              <DropdownMenuContent>
                <DropdownMenuGroup>
                  <DropdownMenuLabel>Account</DropdownMenuLabel>
                  <DropdownMenuItem>
                    <UserIcon /> Profile
                  </DropdownMenuItem>
                  <DropdownMenuItem>
                    <SettingsIcon /> Settings
                  </DropdownMenuItem>
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuItem className="text-destructive focus:text-destructive">
                  <LogOutIcon /> Sign out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            12. POPOVER
        ════════════════════════════════════════════════════════════════ */}
        <Section id="popover" title="12 · Popover">
          <SubSection label="Default">
            <Popover>
              <PopoverTrigger render={<Button variant="outline" size="sm" />}>
                Open popover
              </PopoverTrigger>
              <PopoverContent>
                <PopoverHeader>
                  <PopoverTitle>Popover title</PopoverTitle>
                  <PopoverDescription>A small contextual panel with any content.</PopoverDescription>
                </PopoverHeader>
                <Separator />
                <Button size="sm" className="mt-2.5 w-full">Confirm</Button>
              </PopoverContent>
            </Popover>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            13. DIALOG
        ════════════════════════════════════════════════════════════════ */}
        <Section id="dialog" title="13 · Dialog">
          <SubSection label="Default + footer">
            <Dialog>
              <DialogTrigger render={<Button variant="outline" size="sm" />}>
                Open dialog
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>Confirm action</DialogTitle>
                  <DialogDescription>
                    This will permanently delete the record. This action cannot be undone.
                  </DialogDescription>
                </DialogHeader>
                <DialogFooter showCloseButton>
                  <Button variant="destructive" size="sm">Delete</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            14. SHEET
        ════════════════════════════════════════════════════════════════ */}
        <Section id="sheet" title="14 · Sheet">
          <SubSection label="Right / Left">
            <Sheet>
              <SheetTrigger render={<Button variant="outline" size="sm" />}>
                Right sheet
              </SheetTrigger>
              <SheetContent side="right">
                <SheetHeader>
                  <SheetTitle>Right sheet</SheetTitle>
                  <SheetDescription>A slide-in panel from the right edge.</SheetDescription>
                </SheetHeader>
                <div className="flex-1 px-4 text-sm text-muted-foreground">
                  Sheet body content area.
                </div>
                <SheetFooter>
                  <Button size="sm" className="w-full">Save changes</Button>
                </SheetFooter>
              </SheetContent>
            </Sheet>
            <Sheet>
              <SheetTrigger render={<Button variant="outline" size="sm" />}>
                Left sheet
              </SheetTrigger>
              <SheetContent side="left">
                <SheetHeader>
                  <SheetTitle>Left sheet</SheetTitle>
                  <SheetDescription>Slides in from the left.</SheetDescription>
                </SheetHeader>
              </SheetContent>
            </Sheet>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            15. SCROLL AREA
        ════════════════════════════════════════════════════════════════ */}
        <Section id="scroll-area" title="15 · Scroll Area">
          <SubSection label="Vertical overflow">
            <ScrollArea className="h-36 w-72 rounded-lg border border-border p-3">
              {Array.from({ length: 20 }, (_, i) => (
                <p key={i} className="text-sm py-0.5">
                  Item {i + 1} — scrollable content row
                </p>
              ))}
            </ScrollArea>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            16. SEPARATOR
        ════════════════════════════════════════════════════════════════ */}
        <Section id="separator" title="16 · Separator">
          <div className="space-y-4 max-w-sm">
            <Separator orientation="horizontal" />
            <div className="flex h-6 items-center gap-3 text-sm">
              <span>Left</span>
              <Separator orientation="vertical" />
              <span>Right</span>
            </div>
          </div>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            17. SONNER (TOAST)
        ════════════════════════════════════════════════════════════════ */}
        <Section id="toast" title="17 · Toast (Sonner)">
          <p className="text-sm text-muted-foreground -mt-2">
            Toaster is mounted globally in <code className="text-xs bg-muted px-1 py-0.5 rounded">providers.tsx</code>.
            Trigger samples:
          </p>
          <SubSection label="Toast variants">
            <Button size="sm" variant="outline" onClick={() => toast.success("Operation completed successfully.")}>
              Success
            </Button>
            <Button size="sm" variant="outline" onClick={() => toast.error("Something went wrong. Please try again.")}>
              Error
            </Button>
            <Button size="sm" variant="outline" onClick={() => toast.info("Your export is being prepared.")}>
              Info
            </Button>
            <Button size="sm" variant="outline" onClick={() => toast.warning("Your session will expire in 5 minutes.")}>
              Warning
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                toast.loading("Uploading…", {
                  id: "upload-demo",
                  duration: 2500,
                })
              }
            >
              Loading
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                toast("Custom action", {
                  action: {
                    label: "Undo",
                    onClick: () => toast.success("Undone!"),
                  },
                })
              }
            >
              With action
            </Button>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            18. DATA TABLE
        ════════════════════════════════════════════════════════════════ */}
        <Section id="data-table" title="18 · DataTable">
          <SubSection label="With data + pagination">
            <div className="w-full">
              <DataTable
                columns={TABLE_COLUMNS}
                data={SAMPLE_ROWS}
                rowKey={(r) => r.id}
                total={42}
                page={tablePage}
                pageSize={5}
                onPageChange={setTablePage}
              />
            </div>
          </SubSection>

          <SubSection label="Loading skeleton">
            <div className="w-full">
              <DataTable
                columns={TABLE_COLUMNS}
                data={undefined}
                rowKey={(r: SampleRow) => r.id}
                isLoading
              />
            </div>
          </SubSection>

          <SubSection label="Empty state">
            <div className="w-full">
              <DataTable
                columns={TABLE_COLUMNS}
                data={[]}
                rowKey={(r: SampleRow) => r.id}
                emptyTitle="No users found"
                emptyDescription="Invite someone to get started."
              />
            </div>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            19. EMPTY STATE
        ════════════════════════════════════════════════════════════════ */}
        <Section id="empty-state" title="19 · EmptyState">
          <div className="grid gap-4 sm:grid-cols-2">
            <Card>
              <CardContent className="p-0">
                <EmptyState
                  title="No notifications"
                  description="You're all caught up. New notifications will appear here."
                />
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-0">
                <EmptyState
                  icon={InboxIcon}
                  title="No files uploaded"
                  description="Drag and drop files here, or use the button below."
                  action={{ label: "Upload a file", onClick: () => toast.info("Upload triggered") }}
                />
              </CardContent>
            </Card>
          </div>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            20. MONEY AMOUNT
        ════════════════════════════════════════════════════════════════ */}
        <Section id="money" title="20 · MoneyAmount">
          <SubSection label="Credits">
            <MoneyAmount value={0} />
            <MoneyAmount value={1250} />
            <MoneyAmount value={1000000} />
          </SubSection>
          <SubSection label="Fiat">
            <MoneyAmount value={9.99} currency="USD" />
            <MoneyAmount value={49.00} currency="EUR" />
            <MoneyAmount value={2490} currency="CZK" />
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            21. PROBLEM DETAILS
        ════════════════════════════════════════════════════════════════ */}
        <Section id="problem-details" title="21 · ProblemDetails">
          <SubSection label="Known error code">
            <div className="w-full max-w-md">
              <ProblemDetails
                error={
                  new ApiError({
                    status: 409,
                    errorCode: "user.email_taken",
                    detail: "A user with this email already exists.",
                  })
                }
              />
            </div>
          </SubSection>
          <SubSection label="Generic / unknown error">
            <div className="w-full max-w-md">
              <ProblemDetails error={new Error("Internal error")} />
            </div>
          </SubSection>
          <SubSection label="Rate-limited (429)">
            <div className="w-full max-w-md">
              <ProblemDetails
                error={
                  new ApiError({ status: 429, errorCode: "rate_limit.exceeded", retryAfter: 30 })
                }
              />
            </div>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            22. OPERATION STATUS
        ════════════════════════════════════════════════════════════════ */}
        <Section id="operation-status" title="22 · OperationStatus">
          <p className="text-sm text-muted-foreground -mt-2">
            Rendered with static props (no real polling). The live component reads
            from TanStack Query at 2-second intervals.
          </p>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {(
              [
                { label: "Pending", status: "Pending" as const },
                { label: "Running", status: "Running" as const },
                { label: "Succeeded", status: "Succeeded" as const },
                { label: "Failed", status: "Failed" as const, errorDetail: "Upstream service unavailable." },
              ] satisfies Array<{ label: string; status: Parameters<typeof FakeOperationStatus>[0]["status"]; errorDetail?: string }>
            ).map(({ label, status, errorDetail }) => (
              <Card key={label} size="sm">
                <CardHeader>
                  <CardTitle className="text-xs text-muted-foreground">{label}</CardTitle>
                </CardHeader>
                <CardContent>
                  <FakeOperationStatus status={status} errorDetail={errorDetail} />
                </CardContent>
              </Card>
            ))}
          </div>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            23. REALTIME INDICATOR
        ════════════════════════════════════════════════════════════════ */}
        <Section id="realtime-indicator" title="23 · RealtimeIndicator">
          <p className="text-sm text-muted-foreground -mt-2">
            The live <code className="text-xs bg-muted px-1 py-0.5 rounded">RealtimeIndicator</code> in the
            topbar reads the real SSE connection status. Below are static state previews.
          </p>
          <SubSection label="All connection states">
            <div className="flex flex-col gap-3">
              <div className="flex items-center gap-4">
                <span className="w-24 text-xs text-muted-foreground">open</span>
                <FakeRealtimeIndicator status="open" />
              </div>
              <div className="flex items-center gap-4">
                <span className="w-24 text-xs text-muted-foreground">connecting</span>
                <FakeRealtimeIndicator status="connecting" />
              </div>
              <div className="flex items-center gap-4">
                <span className="w-24 text-xs text-muted-foreground">closed</span>
                <FakeRealtimeIndicator status="closed" />
              </div>
            </div>
          </SubSection>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            24. COMPOSED FORM PATTERN
        ════════════════════════════════════════════════════════════════ */}
        <Section id="form-pattern" title="24 · Composed Form Pattern">
          <p className="text-sm text-muted-foreground -mt-2">
            A realistic invite form demonstrating input + label + checkbox + button + error feedback
            as they appear in production slices (react-hook-form + zod in real usage).
          </p>
          <Card className="max-w-md">
            <CardHeader>
              <CardTitle>Invite team member</CardTitle>
              <CardDescription>Send an invitation link to a new member&apos;s email address.</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="fi-email">Email address</Label>
                <Input id="fi-email" type="email" placeholder="alice@example.com" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="fi-role">Role</Label>
                <Select>
                  <SelectTrigger id="fi-role" className="w-full">
                    <SelectValue placeholder="Select a role" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="member">Member</SelectItem>
                    <SelectItem value="admin">Admin</SelectItem>
                    <SelectItem value="viewer">Viewer</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="fi-message">Personal message (optional)</Label>
                <Textarea id="fi-message" placeholder="Hi, I'd like to invite you to…" rows={2} />
              </div>
              <div className="flex items-start gap-2">
                <Checkbox id="fi-notify" defaultChecked />
                <Label htmlFor="fi-notify" className="leading-snug font-normal cursor-pointer">
                  Notify me when the invite is accepted
                </Label>
              </div>
              {/* Error state example */}
              <Alert variant="destructive">
                <OctagonXIcon />
                <AlertTitle>Validation error</AlertTitle>
                <AlertDescription>
                  That email address is already a member of this workspace.
                </AlertDescription>
              </Alert>
            </CardContent>
            <CardFooter>
              <Button variant="outline" size="sm">Cancel</Button>
              <Button size="sm" className="ml-auto">
                Send invite
              </Button>
            </CardFooter>
          </Card>
        </Section>

        {/* ════════════════════════════════════════════════════════════════
            25. DARK MODE NOTE
        ════════════════════════════════════════════════════════════════ */}
        <Section id="dark-mode" title="25 · Dark Mode">
          <Card className="max-w-lg">
            <CardHeader>
              <CardTitle>ThemeToggle is live in the topbar</CardTitle>
              <CardDescription>
                All tokens flip automatically between light and dark via the{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">.dark</code> class on{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">&lt;html&gt;</code>.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              <p className="text-sm text-muted-foreground">
                OKLCH tokens are defined in{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">app/globals.css</code> under{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">:root</code> (light) and{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">.dark</code>. The{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">@custom-variant dark</code>{" "}
                Tailwind v4 directive maps{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">dark:</code> utilities to{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">.dark *</code> selector.
              </p>
              <p className="text-sm text-muted-foreground">
                Rendered entirely with semantic pairs —{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">bg-background</code>,{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">text-foreground</code>,{" "}
                <code className="text-xs bg-muted px-1 py-0.5 rounded">bg-card</code> etc. — so this
                gallery page adapts to dark mode without any extra work.
              </p>
              <div className="flex flex-wrap gap-2 pt-1">
                <Badge variant="default">default</Badge>
                <Badge variant="secondary">secondary</Badge>
                <Badge variant="outline">outline</Badge>
                <Badge variant="destructive">destructive</Badge>
              </div>
            </CardContent>
          </Card>
        </Section>

        {/* bottom spacer */}
        <div className="h-16" />
      </div>
    </div>
  );
}
