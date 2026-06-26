# ModularPlatform UseCases

Datum: 2026-06-25

Tento dokument je katalog pro noveho vyvojare, ktery stavi modul nad ModularPlatform, napriklad CRM.

Format je zamerne prakticky:

- **UC** = use case, co chce produkt nebo modul udelat.
- **Pouzijes** = hotova cast platformy, kterou mas zavolat.
- **Napises** = co patri do noveho modulu.
- **EC** = edge cases primo k danemu UC, ne obecny seznam bokem.

Pravidlo pro cteni: kdyz delas CRM modul, CRM vlastni jen CRM domenu. Identity, tenanty, kredity, soubory, notifikace, GDPR, realtime, outbox, worker a audit uz existuji v base.

## Identity

### UC01 Registrace usera

**Status:** Implemented + Verified 2026-06-25 — EC001/EC002 overuje `Duplicate_email_registration_is_conflict_and_creates_exactly_one_user`, EC003 overuje `Register_endpoint_uses_the_auth_rate_limit_policy`, EC004 overuje `Registration_does_not_store_the_plaintext_email_in_the_tenant_name`, EC005 overuji `Registering_a_user_provisions_a_credit_account_via_the_event` a `Register_creates_welcome_notification_after_seeder_has_seeded_the_template`.

**Pouzijes:** `POST /identity/users`.

**Co se stane:** Identity vytvori usera, zalozi tenant, vyda tokeny a pres outbox publikuje `UserRegisteredIntegrationEvent`.

**Napises v CRM:** nic do registrace. Pokud CRM potrebuje onboarding, napises handler na `UserRegisteredIntegrationEvent`.

**EC:**

- EC001 duplicitni email vrati konflikt pres blind index.
- EC002 soubezna registrace stejneho emailu skonci unique constraintem, ne dvema usery.
- EC003 register endpoint musi byt rate-limited, protoze dela drahou auth praci.
- EC004 tenant name nesmi obsahovat email ani PII.
- EC005 downstream handlery na registraci musi byt idempotentni, protoze event muze byt retrynuty.

### UC02 Login

**Status:** Implemented + Verified 2026-06-25 — EC006 overuje `Unknown_email_and_wrong_password_return_identical_401_invalid_credentials`, EC007 overuje `Locks_out_after_threshold_failures_and_rejects_correct_credentials`, EC008 overuji `Login_is_rejected_when_the_account_is_soft_deleted` a `Erasure_tombstones_the_row_blanks_the_password_and_kills_the_ciphertext`, EC009 je pokryte frontend scenari `wrong password shows generic error` + `non-existent email shows same generic error (no enumeration)`, EC010 overuje `Permission_gated_endpoint_rejects_non_admins_and_admins_can_grant_roles`.

**Pouzijes:** `POST /identity/auth/login`.

**Co se stane:** Identity overi heslo, zkontroluje lockout, vyda access token a refresh token.

**Napises v CRM:** nic. CRM nikdy neoveruje heslo a nikdy nevydava JWT.

**EC:**

- EC006 neexistujici email a spatne heslo musi vypadat stejne.
- EC007 opakovane spatne pokusy zamknou ucet.
- EC008 soft-deleted nebo erased user se nesmi prihlasit.
- EC009 frontend nesmi ukazovat text typu "email existuje".
- EC010 admin bootstrap patri do Identity login flow, ne do CRM.

### UC03 Refresh session

**Status:** Implemented + Verified 2026-06-25 — EC011 overuje `Refresh_reuse_revokes_whole_family_and_is_audited`, EC012 overuje `Parallel_refresh_with_same_token_yields_one_winner_no_server_error`, EC013 overuji `Refresh_is_rejected_when_the_account_is_soft_deleted` a `Erasure_revokes_all_of_the_subjects_refresh_tokens`, EC014 overuje `Refreshed_token_carries_role_changes_while_the_old_access_token_stays_a_snapshot`, EC015 je implementovane ve frontend BFF flow `refreshSession`/`clearSession` + browser `apiFetch` 401 redirect.

**Pouzijes:** `POST /identity/auth/refresh`.

**Co se stane:** Refresh token se rotuje a token claims se znovu nactou z aktualnich roli a permissions.

**Napises v CRM:** nic, jen pocitas s tim, ze permissions jsou snapshot v access tokenu.

**EC:**

- EC011 replay stareho refresh tokenu revoke celou token family.
- EC012 pri soubeznem refreshi vyhraje jen jeden request.
- EC013 refresh po erasure nebo soft-delete vraci 401.
- EC014 zmena role se projevi az po refreshi/loginu nebo expiraci stareho access tokenu.
- EC015 frontend musi po 401 zahodit session a cache.

### UC04 Logout

**Status:** Implemented + Verified 2026-06-25 — EC016 overuje `Logout_with_another_users_refresh_token_is_a_silent_noop`, EC017 overuje `Logout_with_an_unknown_refresh_token_is_a_silent_success`, EC018 overuje `Logout_without_authentication_is_rejected`, EC019 je implementovane ve frontend `logoutAction` (`session.destroy`, CSRF cookie delete) a pokryte scenari `user menu sign-out redirects to login` / `after logout session cookie is cleared and protected routes redirect to login`, EC020 overuje `Logout_revokes_the_session_family`.

**Pouzijes:** `POST /identity/auth/logout`.

**Co se stane:** Identity revoke refresh-token family. Logout je idempotentni.

**Napises v CRM:** nic.

**EC:**

- EC016 cizi refresh token nesmi odhlasit jineho usera.
- EC017 neznamy refresh token je tichy success.
- EC018 logout bez autentizace vraci 401.
- EC019 frontend po logoutu cisti user-specific cache.
- EC020 client-side smazani tokenu neni nahrada server revoke.

### UC05 Zobrazit profil

**Status:** Implemented + Verified 2026-06-25 — EC021 overuje `Anonymous_caller_with_no_tenant_claim_is_rejected_not_granted_global_visibility`, EC022 overuje `My_profile_is_not_returned_after_the_account_is_soft_deleted`, EC023 overuje `Email_and_display_name_are_ciphertext_at_rest_but_plaintext_through_the_api`, EC024 overuje `A_user_reading_through_the_tenant_filter_sees_only_their_own_tenant_data`, EC025 overuje `My_profile_ignores_any_client_supplied_user_id`.

**Pouzijes:** `GET /identity/users/me`, na frontendu `accountQueries.profile()`.

**Co se stane:** Identity vezme user id z tokenu a vrati profil.

**Napises v CRM:** nic. Nepises vlastni `/crm/me`.

**EC:**

- EC021 request bez tokenu vraci 401.
- EC022 erased/deleted user nema vratit normalni profil.
- EC023 encrypted `Email` a `DisplayName` se ctou pres converter.
- EC024 tenant filter nesmi byt obejity.
- EC025 klient nikdy neposila user id pro vlastni profil.

### UC06 Editace profilu

**Status:** Implemented + Verified 2026-06-25 — EC026 overuje `Update_profile_normalizes_blank_display_name_and_persists_locale`, EC027 overuje `Update_profile_rejects_unsupported_locale`, EC028 je implementovane ve frontend `ProfileForm` pres `queryClient.invalidateQueries({ queryKey: accountQueries.profile().queryKey })`, EC029 overuje `Concurrent_profile_updates_are_serialized_without_server_errors`, EC030 zustava bez eventu/outboxu v `UpdateProfileHandler` a update vraci jen `UserProfileResponse`.

**Pouzijes:** `PATCH /identity/users/me`, na frontendu `updateProfile`.

**Co se stane:** Identity ulozi `DisplayName` a `Locale`.

**Napises v CRM:** nic. Pokud CRM zobrazuje jmeno usera, cte public profile DTO nebo drzi projekci.

**EC:**

- EC026 prazdne nebo whitespace display name se normalizuje na `null`.
- EC027 locale musi projit validaci.
- EC028 po ulozeni invaliduj `accountQueries.profile()`.
- EC029 soubezne editace serializuje xmin a retry behavior.
- EC030 profile update nepousti event, dokud neni skutecny consumer.

### UC07 Zmena hesla

**Status:** Implemented + Verified 2026-06-25 — EC031 overuje `Change_password_rejects_wrong_current_password`, EC032 overuje `Change_password_rejects_weak_new_password`, EC033 overuje `Successful_password_change_revokes_existing_refresh_tokens_and_accepts_only_the_new_password`, EC034 je implementovane ve frontend `ChangePasswordForm` pres `logoutAction()` a redirect na `/login?reason=password-changed`, EC035 overuje `Change_password_ignores_any_client_supplied_user_id`.

**Pouzijes:** `POST /identity/users/me/change-password`.

**Co se stane:** Identity overi current password, ulozi nove heslo a revoke sessions.

**Napises v CRM:** nic, jen frontend po uspechu posle usera na login.

**EC:**

- EC031 spatne current password vraci generic 401.
- EC032 slabe nove heslo vraci validation error.
- EC033 po uspechu uz stara session nema pokracovat.
- EC034 frontend musi vycistit auth state a query cache.
- EC035 zmena hesla nesmi byt dostupna pres cizi user id.

### UC08 Admin priradi roli

**Status:** Implemented + Verified 2026-06-25 — EC036 overuje `Permission_gated_endpoint_rejects_non_admins_and_admins_can_grant_roles`, EC037 overuje `Assign_role_returns_not_found_for_unknown_user_or_role`, EC038 overuje `Concurrent_identical_role_grants_are_idempotent_not_a_500`, EC039 overuje `Refreshed_token_carries_role_changes_while_the_old_access_token_stays_a_snapshot`, EC040 je architektonicky pokryte tim, ze role assignment patri do Identity `AssignRole`/`user_roles`; CRM prida jen permission constants, ne vlastni UserRole store.

**Pouzijes:** `POST /identity/admin/users/{userId}/roles`.

**Co se stane:** Identity prida userovi roli a tim i permissions do dalsiho token snapshotu.

**Napises v CRM:** jen nove permission constants typu `crm.read`, `crm.write`, pokud CRM potrebuje vlastni gate.

**EC:**

- EC036 endpoint vyzaduje admin permission.
- EC037 neexistujici user nebo role vraci 404.
- EC038 duplicate assign je idempotentni.
- EC039 uz vydany access token muze zustat stale do expirace.
- EC040 CRM nepise vlastni `UserRole` tabulku.

### UC09 Admin odebere roli

**Status:** Implemented + Verified 2026-06-25 — EC041 a EC042 overuje `Revoke_role_is_idempotent_and_removes_permission_only_from_new_tokens`; EC043 je pokryte frontend hookem `useRevokeRole`, ktery po uspechu invaliduje `queryRoots.admin`, takze admin user detail query pod timto rootem refetchne; EC044 overuje `Revoke_role_is_a_tracked_delete_that_writes_audit`; EC045 je pokryte implementaci `RevokeRoleHandler`, ktery pouziva tracked `Remove` + `SaveChangesAsync`, ne bulk `ExecuteUpdate`.

**Pouzijes:** `DELETE /identity/admin/users/{userId}/roles/{role}`.

**Co se stane:** Identity odebere roli. Nove tokeny uz permission nemaji.

**Napises v CRM:** nic.

**EC:**

- EC041 odebrani neexistujici role assignment je no-op.
- EC042 stary access token muze jeste chvili fungovat.
- EC043 frontend po zmene roli refetchuje user detail.
- EC044 security zmeny musi byt auditovane pres tracked save.
- EC045 nepouzivat bulk `ExecuteUpdate` na auditovane security rows.

### UC10 Admin zobrazi user detail

**Status:** Implemented + Verified 2026-06-25 — EC046 a EC050 overuje `Get_user_detail_requires_permission_and_returns_projected_current_roles`; EC047 overuje `Get_user_detail_is_tenant_scoped_and_hides_soft_deleted_users` cross-tenant casti; EC048 overuje stejny test pres soft-deleted usera; EC049 je pokryte tim, ze endpoint vraci `UserDetailResponse` DTO/projekci a frontend pouziva `UserDetailResponse`, zadny CRM ani frontend nebere Identity Core typ.

**Pouzijes:** `GET /identity/admin/users/{userId}`.

**Co se stane:** Identity vrati detail usera pro admin pohled.

**Napises v CRM:** CRM drzi jen `UserId`, ne `User` Core entity.

**EC:**

- EC046 bez permission vraci 403.
- EC047 tenant admin a platform admin scope nejsou totez.
- EC048 soft-deleted user se zobrazuje jen tam, kde to endpoint explicitne dela.
- EC049 CRM nesmi referencovat Identity Core.
- EC050 pro UI pouzij DTO nebo projekci.

### UC11 Machine token

**Status:** Implemented + Verified 2026-06-25 — EC051, EC052, EC053, EC054 a EC055 overuje `Admin_mints_a_tenant_scoped_machine_token`: token ma `role=machine`, nema normalni `UserId` context v `HttpTenantContext`, nema implicitni permission claims, ma expiraci, zapisuje tracked `MachineTokenIssuance` metadata + audit row a plaintext JWT neni ulozeny v tabulce ani audit JSON; `A_non_admin_cannot_mint_a_machine_token` overuje permission gate pro vydani.

**Pouzijes:** `POST /identity/admin/machine-tokens`.

**Co se stane:** Identity vyda token pro integraci nebo automat.

**Napises v CRM:** jen endpointy, ktere machine token smi volat, a permissions pro ne.

**EC:**

- EC051 machine token nema automaticky znamenat normalni user context.
- EC052 token musi mit omezene permissions.
- EC053 vydani tokenu musi byt auditovatelne.
- EC054 token musi mit expiraci/rotaci.
- EC055 token se neuklada plaintext do CRM dat.

### UC12 Audit usera v tenant scope

**Status:** Implemented + Verified 2026-06-25 — EC056 overuji `User_pii_is_enveloped_in_audit_and_decryptable_by_an_admin` a `Erasing_the_subject_makes_audit_pii_unrecoverable`; EC057 a tenant-scope cast EC058 overuje `Tenant_audit_requires_permission_and_does_not_cross_tenant`; EC058/EC059 jsou implementovane oddelenim tenant endpointu `/identity/admin/users/{userId}/audit` a platform endpointu `/identity/platform/users/{userId}/audit`, bez centralniho cross-module audit modulu; EC060 je pokryte `GetUserAuditTrailHandler`, ktery cte `db.AuditEntries` pres EF/LINQ a nepouziva raw SQL.

**Pouzijes:** `GET /identity/admin/users/{userId}/audit`.

**Co se stane:** Identity vrati audit trail usera pro admina.

**Napises v CRM:** per-module audit endpoint jen pro CRM entity, pokud je potreba.

**EC:**

- EC056 po crypto-shred se PII v auditu zobrazi jako `[erased]`.
- EC057 audit read vyzaduje permission.
- EC058 audit neni centralni cross-module DB.
- EC059 CRM audit nesmi cist cizi module audit tabulky.
- EC060 raw SQL pro audit je zakazany.

### UC13 Platform admin listuje usery

**Status:** Implemented + Verified 2026-06-25 — EC061 a EC063 overuje `Platform_user_list_requires_permission_and_returns_limited_page`; EC064 a soft-delete guard overuje `Platform_user_list_filters_by_tenant_and_hides_soft_deleted_users`; EC062 je implementovane v explicitnim `Features/PlatformAdmin/ListPlatformUsers` handleru pres `IgnoreQueryFilters()` + znovu pridany `DeletedAt == null`; EC065 je implementovane explicitnim structured logem `Platform user list accessed tenantId={TenantId} limit={Limit} offset={Offset}` bez PII.

**Pouzijes:** `GET /identity/platform/users`.

**Co se stane:** Platform admin vidi usery napric tenanty.

**Napises v CRM:** nic. Bezny CRM list nikdy nema obchazet tenant filter.

**EC:**

- EC061 endpoint je jen platform-admin.
- EC062 `IgnoreQueryFilters` se pouziva jen v explicitnim platform-admin flow.
- EC063 list ma limit a paging.
- EC064 filtr tenantem musi byt explicitni.
- EC065 pristup do platform listu je audit/log concern.

### UC14 Platform admin cte platform audit usera

**Status:** Implemented + Verified 2026-06-25 — EC066 a EC067 overuje `Platform_audit_requires_platform_permission_and_keeps_erased_pii_unreadable`; EC068 overuje spolu s `Tenant_audit_requires_permission_and_does_not_cross_tenant`, protoze tenant endpoint zustava `/identity/admin/users/{userId}/audit` a platform endpoint je explicitni `/identity/platform/users/{userId}/audit`; EC069 je pokryte tim, ze platform endpoint jen dispatchuje `GetUserAuditTrailQuery(CrossTenant: true)` a handler cte Identity `db.AuditEntries`, ne cizi module Core ani cizi audit tabulky; EC070 zustava zachovane pres `IPersonalDataProtector`, crypto-shred a `[erased]` marker.

**Pouzijes:** `GET /identity/platform/users/{userId}/audit`.

**Co se stane:** Platform admin vidi audit mimo tenant scope.

**Napises v CRM:** nic.

**EC:**

- EC066 jen platform permission.
- EC067 erased PII zustane unreadable.
- EC068 neplest s tenant admin audit endpointem.
- EC069 zadne cross-module joiny.
- EC070 respektovat retention a GDPR pravidla.

## Tenancy

### UC15 Zjistit moje entitlements

**Status:** Implemented — overeno testy `EntitlementsTests` + `EntitlementGuardCoverageTests` a frontend
`pnpm typecheck`.

**Pouzijes:** `GET /tenant/me/entitlements`.

**Co se stane:** Frontend zjisti, ktere moduly ma tenant zapnute.

**Napises v CRM:** navigation guard a backend `.RequireModule("crm")`.

**EC:**

- EC071 chybejici entitlement schova CRM menu: frontend `isModuleEnabled` vraci false pro chybejici modul.
- EC072 backend stale blokuje endpoint, i kdyz UI menu nekdo obejde: test vypne `billing` a primy call na
  `/billing/packages` skonci 404.
- EC073 po admin zmene invaliduj entitlement cache: `useSetEntitlement` invaliduje `queryRoots.entitlements`.
- EC074 menu guard neni security: security je `.RequireModule(...)`, ne navigace.
- EC075 novy modul ma byt defaultne vypnuty, dokud neni entitlement: test overuje, ze cerstvy tenant nema `crm`.

### UC16 Platform admin vytvori tenant

**Status:** Implemented — overeno `PlatformAdminTests`.

**Pouzijes:** `POST /tenant/admin/tenants`.

**Co se stane:** Tenancy vytvori tenant a publikuje `TenantProvisionedIntegrationEvent`.

**Napises v CRM:** nic, pripadne handler na tenant provisioned pro vlastni seed.

**EC:**

- EC076 jen platform admin: bez `platform.tenants.manage` vraci endpoint 403.
- EC077 duplicate tenant key/subdomain: stejny subdomain vrati 409 a nevznikne druhy tenant.
- EC078 provisioning musi byt atomicky: po uspechu existuje tenant i default entitlements ve stejne platform flow.
- EC079 CRM neprovisionuje tenant bokem: CRM si tenant nevytvari; pokud potrebuje seed, reaguje na
  `TenantProvisionedIntegrationEvent`.
- EC080 CRM seed handler musi byt idempotentni: budoucí CRM handler musi pouzit vlastni unique key / upsert podle
  `TenantId`, protoze event muze prijit znovu.

### UC17 Platform admin listuje tenanty

**Status:** Implemented — overeno `PlatformAdminTests`.

**Pouzijes:** `GET /tenant/admin/tenants`.

**Co se stane:** Admin UI vidi seznam tenantu.

**Napises v CRM:** nic.

**EC:**

- EC081 paging a limit: endpoint vraci `limit`, `offset`, `total` a neprekryvajici se stranky.
- EC082 platform-admin only: bez `platform.tenants.manage` vraci endpoint 403.
- EC083 nepouzivat v beznem CRM tenant UI: je to platform-admin registry list, ne tenant CRM obrazovka.
- EC084 list nesmi leakovat citliva data: response shape neobsahuje `dbDsnSecretRef`, `infraRevision`, moduly ani
  entitlements.
- EC085 stale list po provision nebo entitlement change: `useProvisionTenant` i `useSetEntitlement` invaliduji
  `queryRoots.admin`.

### UC18 Tenant detail

**Status:** Implemented — overeno `PlatformAdminTests`.

**Pouzijes:** `GET /tenant/admin/tenants/{tenantId}`.

**Co se stane:** Admin vidi detail tenant nastaveni.

**Napises v CRM:** nic, CRM nema delat kopii tenant detailu bez duvodu.

**EC:**

- EC086 tenant not found -> 404: chybejici tenant vraci `tenant.not_found`.
- EC087 platform-admin scope: bez `platform.tenants.manage` vraci endpoint 403.
- EC088 detail neni CRM config source: jde o platform-admin registry detail, CRM si drzi vlastni CRM config.
- EC089 zobrazit entitlements jasne: detail vraci `modules[]` s `key`, `enabled`, `tier` z persisted entitlements.
- EC090 CRM nesmi prepisovat tenant metadata: CRM nesaha na `tenants`; pouziva jen `TenantId` jako referenci.

### UC19 Zapnout nebo vypnout modul tenantovi

**Status:** Implemented — overeno `PlatformAdminTests`, `EntitlementsTests` a frontend `useSetEntitlement`.

**Pouzijes:** `PUT /tenant/admin/tenants/{tenantId}/entitlements/{moduleKey}`.

**Co se stane:** Tenancy zmeni entitlement pro modul.

**Napises v CRM:** endpointy chranis `.RequireModule("crm")`.

**EC:**

- EC091 preklep v module key: neznamy key vrati 422 `tenant.module_unknown` a neulozi typo radek.
- EC092 vypnuti CRM nema mazat CRM data automaticky: entitlement toggle meni jen `tenant_entitlements`, ne data modulu.
- EC093 UI musi refetchnout entitlements: `useSetEntitlement` invaliduje `queryRoots.admin` i
  `queryRoots.entitlements`.
- EC094 backend guard je autorita: `.RequireModule(...)` cte entitlement live a blokuje primy bypass.
- EC095 zmena entitlementu musi byt auditovatelna: update `TenantEntitlement` zapisuje `tenancy_audit_entries`.

### UC20 Tenant invite

**Status:** Implemented — overeno `RegistrationJoinTests`.

**Pouzijes:** `POST /tenant/admin/tenants/{tenantId}/invites`.

**Co se stane:** Tenancy vytvori invite pro pridani usera do tenant kontextu.

**Napises v CRM:** nic.

**EC:**

- EC096 expired invite: expired token neprojde pres tenant registration gate.
- EC097 invite reuse: token je single-use, po prvnim joinu je dalsi pokus 403.
- EC098 invite pro cizi tenant: token je vázany na `TenantId`, cizi subdomain ho neakceptuje.
- EC099 invite email/notifikace patri do platform flow: CRM nema posilat vlastni invite tokeny; muze jen spustit
  platformovou notifikaci, pokud produkt chce e-mail.
- EC100 CRM negeneruje invite tokeny: token mintuje jen Tenancy `POST /tenant/admin/tenants/{tenantId}/invites`.

### UC21 Platform billing status

**Status:** Implemented — overeno `PlatformBillingStatusTests` a frontend `PlatformBillingCard`.

**Pouzijes:** `GET /tenant/admin/platform-billing`.

**Co se stane:** Admin vidi, jestli je platform billing pripraveny.

**Napises v CRM:** nic.

**EC:**

- EC101 missing platform payment config: status vrati `checkoutReady=false` a `actionRequired`.
- EC102 provider down: status neprovadi checkout; provider problem mapuje na `checkoutReady=false`.
- EC103 UI musi ukazat actionable stav: `PlatformBillingCard` zobrazuje ready/off stav a `actionRequired`.
- EC104 platform-plane billing a tenant-plane billing nejsou totez: status pouziva `PaymentPlane.Platform`.
- EC105 nespoustet checkout naslepo bez statusu: frontend ma cist status pred povolenim checkout akce.

### UC22 Platform checkout

**Status:** Implemented — overeno `PlatformCheckoutTests`.

**Pouzijes:** `POST /tenant/me/platform-checkout`.

**Co se stane:** Tenant user/admin jde do checkoutu za platform subscription.

**Napises v CRM:** CRM jen respektuje vysledny entitlement.

**EC:**

- EC106 tenant bez provider config: checkout vrati 422, kdyz `PaymentPlane.Platform` nema gateway.
- EC107 checkout session expired: expired/failed session se resi pres provider stav/webhook, ne pres redirect URL.
- EC108 webhook prijde pozdeji nez navrat usera: checkout vraci jen redirect; stav/entitlement se cte pozdeji z platformy.
- EC109 entitlement se zapne az po potvrzeni: samotne vytvoreni checkoutu nemeni `tenant_entitlements`.
- EC110 CRM nikdy nebere checkout success jako trvaly entitlement: CRM respektuje jen live entitlement guard/status.

## Billing

### UC23 Nastavit tenant payment gateway

**Status:** Implemented — overeno `PaymentGatewayConfigTests`.

**Pouzijes:** `PUT /billing/payment-gateway`.

**Co se stane:** Billing ulozi provider config a secrets pres platform secrets.

**Napises v CRM:** nic, CRM neskladuje payment secrets.

**EC:**

- EC111 jen opravneny admin: bez `billing.manage` vraci endpoint 403.
- EC112 secret se nesmi ulozit plaintext: Stripe key je ulozeny jako sealed ciphertext v `tenant_secrets`.
- EC113 unsupported provider: neznamy provider vrati 422.
- EC114 fake gateway jen v test/dev rezimu: Production host `fake` odmita.
- EC115 po zmene invalidovat billing config UI: config UI zatim v repu neni; az vznikne, mutation musi invalidovat
  `queryRoots.billing`.

### UC24 Vytvorit tenant checkout

**Status:** Implemented — overeno `CreateTenantCheckoutTests`; CRM vola platformni endpoint, ne payment SDK.

**Pouzijes:** `POST /billing/payments/checkout`.

**Co se stane:** Billing vybere provider podle tenant configu a vytvori checkout.

**Napises v CRM:** jen redirect/link na hotovy checkout, pokud CRM prodava tenant-plane vec.

**EC:**

- EC116 provider config missing → cisty 422 `payment.gateway_not_configured`, zadny fallback na cizi tenant gateway.
- EC117 amount nebo currency invalid → validator vrati 400 pred volanim providera.
- EC118 checkout expired → provider stav se resi pres webhook/re-fetch, CRM ma znovu zavolat checkout endpoint a nedrzet vlastni expiraci.
- EC119 provider failure ma byt user-friendly error → runtime failure gateway se mapuje na 422 `payment.gateway_inactive`, ne 500.
- EC120 CRM nevola Stripe/GoPay SDK primo → CRM dostane jen `providerPaymentId` a `redirectUrl`.

### UC25 Tenant payment webhook

**Status:** Implemented — overeno `TenantWebhookTests`; webhook je anonymni provider callback, ale handler veri/re-fetchuje pres tenant gateway.

**Pouzijes:** `POST /billing/webhooks/{provider}/{tenantId}/{token?}`.

**Co se stane:** Billing prijme webhook pro tenant-plane provider.

**Napises v CRM:** nic.

**EC:**

- EC121 spatny token nebo signature → GoPay token mismatch a Stripe bad signature se acknowledge+ignore, nic se neprovede.
- EC122 duplicate webhook → stejny paid webhook nevytvori druhy ledger entry.
- EC123 unknown tenant → 200 OK a ignore, zadny fallback na jinou gateway.
- EC124 out-of-order event → unpaid webhook nic negrantuje; pozdejsi paid webhook purchase dokonci.
- EC125 handler musi byt idempotentni → grant jde pres `CreditPurchaseSaga` a ledger idempotency key `purchase:{id}`.

### UC26 Stripe platform webhook

**Status:** Implemented — overeno `StripeWebhookTests`, `BillingCommerceTests`, `DeadLetterTests`, `StripeReconcileTests`.

**Pouzijes:** `POST /billing/webhooks/stripe`.

**Co se stane:** Billing prijme Stripe event a routuje ho do commandu/sagy.

**Napises v CRM:** nic.

**EC:**

- EC126 invalid signature → 400 pred persistenci, nic se neulozi.
- EC127 redelivery → stejny signed event id je ingest exactly-once pres unique `stripe_events`.
- EC128 unknown event type → worker refetchne event, oznaci `ProcessedAt`, ale neprovede ledger side effect.
- EC129 event router refetchuje live state → test posila minimal payload, handler bere skutecny event z `IStripeGateway`.
- EC130 stuck event resi retry/DLQ/reconcile → failing worker message jde do DLQ; reconcile requeue/regrant flow je otestovany.

### UC27 Credit balance

**Status:** Implemented — overeno `CreditBalanceTests`, `LedgerBackstopTests`, `RlsTests`; response vraci `posted/pending/available`.

**Pouzijes:** `GET /billing/credits/balance` nebo public query.

**Co se stane:** Billing vrati aktualni projection `posted/pending/available`.

**Napises v CRM:** UI muze zobrazit balance, ale backend placene akce vzdy vola reserve.

**EC:**

- EC131 chybejici account → registrace provisionuje zero account; primy query bez accountu vraci `credit.account_not_found`.
- EC132 UI balance je orientacni, neni security → placene akce musi jit pres reserve/confirm/release, ne pres UI hodnotu.
- EC133 po reserve/confirm/release invaliduj balance → endpoint vraci ulozenou projekci po kazde mutaci.
- EC134 balance je user/tenant scoped → endpoint bere usera z tokenu; RLS brani cteni ciziho accountu.
- EC135 CRM nepocita balance ručně z ledgeru → CRM vola `GET /billing/credits/balance` nebo Billing query.

### UC28 Credit ledger

**Status:** Implemented — overeno `CreditLedgerReadTests`, `LedgerLifecycleTests`, `GdprIntegrationTests`.

**Pouzijes:** `GET /billing/credits/entries`.

**Co se stane:** Billing vrati append-only ledger entries.

**Napises v CRM:** jen link nebo embed Billing UI.

**EC:**

- EC136 paging → `GET /billing/credits/entries?page=&pageSize=` vraci `PagedResponse`.
- EC137 ledger append-only → money flow pridava `credit_entries`; zmeny stavu jdou pres nove entries/holds, ne prepis ledgeru.
- EC138 GDPR erase ledger fyzicky nemaze → Billing ledger/account rows zustavaji kvuli AML/tax a integrite knihy.
- EC139 ledger PII se anonymizuje podle pravidel → Billing ledger dnes nema free-text PII; erasure probiha crypto-shreddingem subject key.
- EC140 CRM neduplikuje ledger → CRM vola endpoint/query, nema vlastni tabulku ani vypocet ledgeru.

### UC29 Credit top-up

**Status:** Implemented — overeno `CreditTopUpAuthorizationTests`, `BillingLedgerTests`, `CreditAmountBoundsTests`.

**Pouzijes:** `POST /billing/credits/topup`.

**Co se stane:** Billing pripise kredity idempotentne.

**Napises v CRM:** nic, pouzij jen admin/backoffice flow.

**EC:**

- EC141 idempotency key required → validator vraci 400; endpoint prefixuje klientsky key jako `client:{key}`.
- EC142 duplicate top-up nesmi dat dvoji kredit → per-account unique idempotency key vraci `alreadyApplied=true`.
- EC143 amount musi byt kladny → validator odmita `<= 0` i oversized hodnoty.
- EC144 account provision race → handler pri UNIQUE(UserId) race reloadne account; concurrent ensure test drzi invariant.
- EC145 top-up endpoint musi byt permission-gated → HTTP endpoint vyzaduje `billing.manage`; bez nej 403.

### UC30 Reserve credits

**Status:** Implemented — overeno `ReserveCreditsTests`, `LedgerLifecycleTests`, `BillingConcurrencyTests`, `CreditAmountBoundsTests`.

**Pouzijes:** `POST /billing/credits/reservations` nebo command.

**Co se stane:** Billing atomicky zkontroluje `available >= amount` a zalozi hold.

**Napises v CRM:** ulozis `ReservationId`, cenu a stav `Reserved`.

**EC:**

- EC146 insufficient credits -> business error → 422 `credit.insufficient_balance`, bez hold/ledger side effectu.
- EC147 concurrent reserve nesmi double-spend → atomic EF `ExecuteUpdate` guard `WHERE Available >= amount`.
- EC148 amount musi byt kladny → validator odmita `<= 0`, oversized a nekladne `holdMinutes`.
- EC149 hold muze expirovat → expiry sweep prevede lapsed hold na `Expired` a vrati availability.
- EC150 CRM nesmi delat read-then-write balance → CRM vola `ReserveCreditsCommand`/endpoint a uklada jen `ReservationId`.

### UC31 Confirm spend

**Status:** Implemented — overeno `ConfirmSpendTests`, `BillingLedgerTests`, `LedgerLifecycleTests`, `CreditBalanceTests`.

**Pouzijes:** `POST /billing/credits/reservations/confirm`.

**Co se stane:** Billing prevede pending hold do utraty.

**Napises v CRM:** worker vola confirm az po realnem uspechu externi akce.

**EC:**

- EC151 duplicate confirm je idempotentni → concurrent confirm zapise jen jeden `Spend`.
- EC152 hold not found nebo released → unknown reservation je 404, released/expired/non-active hold je 422 bez spendu.
- EC153 bucket draw musi zachovat invariant → confirm kresli buckets FIFO a drzi `available = posted - pending`.
- EC154 worker retry nesmi utratit dvakrat → `spend:{reservationId}` idempotency key + xmin retry.
- EC155 confirm se nesmi volat pred skutecnym uspechem → CRM/worker vola confirm az po uspesne externi akci; jinak vola release.

### UC32 Release hold

**Status:** Implemented — overeno `ReleaseHoldTests`, `LedgerLifecycleTests`, `CreditBalanceTests`.

**Pouzijes:** `POST /billing/credits/reservations/release`.

**Co se stane:** Billing vrati rezervovane kredity do available.

**Napises v CRM:** failure branch a reconcile job volaji release.

**EC:**

- EC156 duplicate release je idempotentni → opakovany release vraci 200 a neprida druhy `Release` entry.
- EC157 release po confirm nesmi vratit kredit → confirmed hold se jen nahlasi jako uz resolved, bez obnovy available.
- EC158 kazda failure vetev musi release resit → CRM failure/retry branch vola `ReleaseHoldCommand`.
- EC159 stuck `Reserved` stav potrebuje reconcile → expiry sweep materializuje lapsed active holds jako `Expired` a vrati availability.
- EC160 UI refetchuje balance po release → handler po commit publikuje `billing.credits_changed`.

### UC33 Public packages

**Status:** Implemented — overeno `PublicPackageCatalogueTests`, `BillingCommerceTests`.

**Pouzijes:** `GET /billing/packages`.

**Co se stane:** Billing vrati koupitelne credit packages.

**Napises v CRM:** nic.

**EC:**

- EC161 disabled package se neprodava → public list vraci jen `Active=true`; checkout inactive package odmita.
- EC162 stabilni sort order → public list radi `Price`, potom `Name`, potom `Id`.
- EC163 cena a kredit jsou Billing source of truth → response nese `creditAmount`, `price`, `currency`, `bucketExpiryDays`.
- EC164 stale cache po admin edit → po update inactive public endpoint package nevrati; FE ma invalidovat billing package query.
- EC165 CRM nehardcoduje package ids → CRM bere ids a ceny z `GET /billing/packages`.

### UC34 Admin package list

**Status:** Implemented — `AdminPackageCatalogueTests` overuji permission, disabled rows, paging metadata, stabilni order a oddeleni public/admin listu.

**Pouzijes:** `GET /billing/admin/packages`.

**Co se stane:** Admin vidi vsechny packages vcetne disabled.

**Napises v CRM:** nic.

**EC:**

- EC166 admin permission → endpoint vyzaduje `billing.manage`, bez nej vraci 403.
- EC167 include disabled → admin list vraci i `active=false` packages.
- EC168 paging/order → `page/pageSize` vraci `PagedResponse`, order je `Price`, potom `Name`, potom `Id`.
- EC169 audit admin change → admin create/update package jde pres audited `CreditPackage` entitu; zmena katalogu je domenova mutace, ne read side effect listu.
- EC170 oddelit admin list a public list → admin list vidi disabled, public `GET /billing/packages` vraci jen aktivni purchasable packages.

### UC35 Create package

**Status:** Implemented — `CreateCreditPackageTests` overuji validace, duplicate name, active flag a viditelnost v admin/public listu.

**Pouzijes:** `POST /billing/admin/packages`.

**Co se stane:** Admin vytvori novy package.

**Napises v CRM:** nic.

**EC:**

- EC171 amount/price validation → `creditAmount > 0`, `price >= 0`.
- EC172 currency validation → 3 pismena ISO tvaru; handler normalizuje na uppercase.
- EC173 duplicate key/name → v jednom katalogu nejde zalozit package se stejnym jmenem.
- EC174 disabled/default stav podle business rozhodnuti → `active` je explicitni input; `active=false` zalozi disabled package.
- EC175 po create invalidovat admin i public list → po create se novy package objevi v admin listu; pokud je active, objevi se i v public listu. FE musi invalidovat oba query cache klice.

### UC36 Update package

**Status:** Implemented — `UpdateCreditPackageTests` overuji not found/foreign scope, historical purchase snapshot, public disable, concurrency a audit.

**Pouzijes:** `PUT /billing/admin/packages/{packageId}`.

**Co se stane:** Admin zmeni package.

**Napises v CRM:** nic.

**EC:**

- EC176 not found → neznamy nebo cizi tenant package vraci 404 bez existence leaku.
- EC177 zmena ceny nesmi prepsat historicke purchases → checkout zalozi snapshot v `credit_purchase_sagas`; pozdejsi update package ho nemeni.
- EC178 disabled package zmizi z public listu → `active=false` se v admin listu drzi, public `GET /billing/packages` ho nevrati.
- EC179 concurrent update → tracked save + xmin/concurrency retry serializuje paralelni update bez 500.
- EC180 audit change → update jde pres tracked `CreditPackage` a pise `billing_audit_entries`.

### UC37 Purchase package checkout

**Status:** Implemented — `PurchaseCreditPackageTests` + existujici `BillingCommerceTests` overuji disabled/not found, duplicate click, gateway unavailable, timeout a confirmed-webhook grant.

**Pouzijes:** `POST /billing/packages/{packageId}/checkout`.

**Co se stane:** Billing zalozi purchase sagou a checkout session.

**Napises v CRM:** redirect do checkoutu.

**EC:**

- EC181 package disabled/not found → unknown package vraci 404, inactive package vraci `billing.package_inactive`.
- EC182 duplicate checkout click → bez client idempotency key backend zalozi dve oddelene pending purchases; FE musi button disableovat. Bez confirmed webhooku se nic nepripise.
- EC183 provider unavailable → tenant bez payment gateway configu dostane `payment.gateway_not_configured`.
- EC184 saga timeout → `CreditPurchaseTimeout` presune pending purchase na `Abandoned`; late confirmation stale muze grantnout.
- EC185 kredit se grantuje az po confirmed webhooku → checkout pouze vytvori provider session + pending saga; ledger entry vznikne az po paid webhooku.

### UC38 Purchase status

**Status:** Implemented — `PurchaseStatusTests` overuji owner scope a stavy `Pending` → `Abandoned` → late `Completed`.

**Pouzijes:** `GET /billing/purchases/{purchaseId}`.

**Co se stane:** Frontend polluje purchase/saga stav.

**Napises v CRM:** nic.

**EC:**

- EC186 foreign purchase -> 404 → endpoint filtruje podle `UserId` z tokenu.
- EC187 pending/confirmed/abandoned states → status se cte ze saga row; `Pending`, `Abandoned`, `Completed`.
- EC188 late webhook after timeout → pozdni `CreditPurchaseConfirmed` po `Abandoned` preklopi purchase na `Completed` a zachova penize.
- EC189 frontend polling interval a loading → FE polluje `GET /billing/purchases/{purchaseId}`; pred materializaci saga row muze kratce dostat 404/loading.
- EC190 CRM necita Billing DB → CRM pouziva endpoint/response, ne `credit_purchase_sagas` tabulku.

### UC39 Subscription plans

**Status:** Implemented — `SubscriptionPlansTests` overuji enabled/valid filtr, empty config, stabilni `planKey` a neexponovani provider price id.

**Pouzijes:** `GET /billing/subscriptions/plans`.

**Co se stane:** Billing vrati config-driven plany.

**Napises v CRM:** jen mapovani plan -> CRM capability, pokud produkt potrebuje.

**EC:**

- EC191 config missing/invalid → endpoint vrati jen validni plany; kdyz neni zadny validni enabled plan, vrati prazdny list.
- EC192 disabled plan → `Enabled=false` plan se v API neukaze.
- EC193 stale frontend cache → FE ma refetchovat/cachovat `GET /billing/subscriptions/plans` jako konfiguracni katalog.
- EC194 plan key musi byt stabilni → response radi podle `planKey`; CRM uklada jen `planKey`, ne Stripe price id.
- EC195 CRM neparsuje raw Billing config → API neposila `StripePriceId`; CRM pouziva response DTO.

### UC40 Subscription checkout

**Status:** Implemented — `SubscriptionCheckoutTests` + existujici subscription lifecycle testy overuji plan guard, active-sub conflict, double checkout a out-of-order webhook.

**Pouzijes:** `POST /billing/subscriptions/checkout`.

**Co se stane:** Billing vytvori subscription checkout.

**Napises v CRM:** redirect a refetch po navratu.

**EC:**

- EC196 existing active subscription → non-canceled local mirror blokuje dalsi checkout pres `billing.subscription.already_active`.
- EC197 checkout session expired → checkout nevytvari lokalni pending subscription; opustena/expired session nezanecha orphan row.
- EC198 webhook out-of-order → `UpsertSubscriptionFromStripeCommand` reconciliuje ze Stripe object state i kdyz `updated` prijde pred `created`.
- EC199 double checkout okno pred webhookem → dva rychle checkouty vytvori dve provider sessions, ale zadnou lokalni subscription pred webhookem.
- EC200 UI disable pending state → CRM/frontend ma po kliknuti disableovat tlacitko a po navratu refetchovat `/billing/subscriptions/me`.

### UC41 My subscription

**Status:** Implemented — `MySubscriptionTests` overuji empty state, `PastDue`, `CancelAtPeriodEnd` a cteni reconciliovaneho mirroru.

**Pouzijes:** `GET /billing/subscriptions/me`.

**Co se stane:** Billing vrati aktualni subscription stav.

**Napises v CRM:** ctes jen pro UI nebo entitlement rozhodnuti pres policy.

**EC:**

- EC201 no subscription -> empty state → endpoint vraci 404 `billing.subscription.not_found`.
- EC202 past_due state → Stripe `past_due/unpaid/paused` se mapuje na `PastDue`.
- EC203 cancel at period end → response nese `cancelAtPeriodEnd`.
- EC204 stale webhook state resi reconcile → lokalni mirror se aktualizuje pres `UpsertSubscriptionFromStripeCommand`/reconcile ze Stripe object state.
- EC205 no CRM-local subscription copy → CRM cte `/billing/subscriptions/me`, nedrzi vlastni subscription tabulku.

### UC42 Cancel subscription

**Status:** Implemented — `CancelSubscriptionTests` overuji no active, provider failure a idempotent cancel-at-period-end.

**Pouzijes:** `POST /billing/subscriptions/cancel`.

**Co se stane:** Billing zavola provider a zmeni stav subscription.

**Napises v CRM:** invalidace subscription a entitlement UI.

**EC:**

- EC206 no active subscription → endpoint vraci 404 `billing.subscription.not_found`.
- EC207 provider failure → provider exception se preklada na domenovou 422 `billing.subscription.provider_failed`.
- EC208 idempotent cancel → opakovany cancel pri `cancelAtPeriodEnd=true` zustava OK a drzi stejny lokalni intent.
- EC209 entitlement revocation timing → default je cancel at period end, status zustava `Active` dokud provider webhook/reconcile nepotvrdi terminal state.
- EC210 invalidovat subscription i entitlements → FE/CRM po cancel refetchuje subscription a entitlement UI; realtime z webhooku invaliduje subscription state.

### UC43 Billing portal

**Status:** Implemented + tested — `BillingPortalTests`.

**Pouzijes:** `POST /billing/portal`.

**Co se stane:** Billing najde Stripe customer id z posledniho subscription zaznamu uzivatele a vytvori hosted Customer Portal session.

**Napises v CRM:** zavolas endpoint bez body, vezmes `data.url` a udelas browser redirect. CRM nikdy neposila `customerId` ani nesklada Stripe portal URL samo.

**EC:**

- EC211 missing customer id → uzivatel jeste nema provider customer id, endpoint vraci 422 `billing.no_billing_account`.
- EC212 provider down → Stripe vyjimka se prelozi na domenovou 422 `billing.portal.provider_failed`, nepropadne jako 500.
- EC213 return URL validation → `Billing:Stripe:SuccessUrl` musi byt absolutni `http(s)` URL, jinak 422 `billing.portal.invalid_return_url`.
- EC214 portal session expired → URL je kratkodoba provider session; pri expiraci CRM znovu zavola `POST /billing/portal` a dostane novou URL.
- EC215 CRM nevytvari portal session samo → customer id se bere ze serverove DB podle tokenu, request nema body a nejde podvrhnout cizi customer id.

### UC44 Promo code

**Status:** Implemented + tested — `PromoCodeTests`.

**Pouzijes:** `GET /billing/promo-codes/{code}/validate`.

**Co se stane:** Billing normalizuje UI vstup, zepta se provideru na aktivni promo code a vrati discount shape pro nahled v UI.

**Napises v CRM:** input s debounce, po 200-500 ms volas validate endpoint. Vysledek je jen UX hint; pri checkoutu se stejne spolehas na provider validaci.

**EC:**

- EC216 invalid/expired code → provider nic nevrati, endpoint vraci 404 `billing.coupon.invalid`.
- EC217 code not applicable → pre-check to nebere jako pravdu pro konkretni kosik; finalni aplikovatelnost resi Stripe Checkout.
- EC218 provider rate limit → Stripe vyjimka se prelozi na 422 `billing.coupon.provider_failed`, UI muze zkusit pozdeji.
- EC219 frontend debounce → CRM nedela request na kazdy keypress; vola az po kratke pauze a rusi stare requesty.
- EC220 checkout stejne validuje na backendu → subscription/package checkout posila `AllowPromotionCodes`, provider znovu overi code a discount math.

### UC45 Stripe reconcile

**Status:** Implemented + tested — `StripeReconcileTests`.

**Pouzijes:** `BillingStripeReconcileJob`.

**Co se stane:** Jobs host pusti `ReconcileStripeCommand`, ktery requeue stuck Stripe events, opravuje subscription drift podle live Stripe state a znovu grantuje prokazatelne zaplacene stuck purchases.

**Napises v CRM:** pro vlastni CRM externi systemy kopiruj pattern: capped sweep, per-item try/catch, provider je source of truth, oprava jde pres stejne commandy jako normalni webhook.

**EC:**

- EC221 stuck `stripe_events` → `ProcessedAt IS NULL` a starsi nez 30 min se znovu publikuji pres outbox jako `ProcessStripeEventMessage`.
- EC222 subscription drift → lokalni non-canceled subscription se porovna s live provider stavem; pri driftu Stripe vyhrava a bezi `UpsertSubscriptionFromStripeCommand`.
- EC223 provider API down → chyba jednoho Stripe lookupu se zaloguje jako warning, sweep pokracuje dalsimi subscriptions/purchases.
- EC224 cap per run → stuck events 200, subscriptions 500, stuck purchases 200; run nikdy nezkusi nekonecny backlog najednou.
- EC225 warning metrics/logs → cap reached a drift se loguji warningem, drift inkrementuje `platform.billing.stripe_drift`.

### UC46 Expirace kreditu

**Status:** Implemented + tested — `LedgerLifecycleTests`.

**Pouzijes:** `BillingExpireCreditsJob`.

**Co se stane:** Jobs host dispatchne `ExpireCreditsCommand`, ktery materializuje expirovane holdy a bucket zmeny do ledgeru a projekce uctu.

**Napises v CRM:** nic. CRM nikdy neupravuje bucket remaining ani hold status; jen cte balance/ledger.

**EC:**

- EC226 expired buckets → bucket s volnym `Remaining` po `ExpiresAt` dostane `Expiry` ledger entry a snizi `Posted`/`Available`.
- EC227 hold overlapping bucket → kdyz bucket kryje aktivni rezervaci, sweep ho preskoci, aby nesrazil balance pod nulu.
- EC228 idempotency key per expiration → ledger pouziva `expire-hold:{holdId}` a `expire-bucket:{bucketId}`, opakovany sweep je no-op.
- EC229 UTC cron → `BillingModule.RegisterJobs` nastavuje Quartz cron pres `InTimeZone(TimeZoneInfo.Utc)` a job ma `DisallowConcurrentExecution`.
- EC230 CRM nesmi upravovat bucket remaining → zmena bucketu jde jen pres Billing commandy; CRM nema endpoint ani tabulkovy zapis na bucket remaining.

### UC47 Provision credit account

**Status:** Implemented + tested — `CrossModuleEventTests`, `LedgerBackstopTests`.

**Pouzijes:** Billing handler na `UserRegisteredIntegrationEvent`.

**Co se stane:** Identity publikuje `UserRegisteredIntegrationEvent`; Billing public Wolverine handler dispatchne internal `EnsureCreditAccountCommand` a vytvori zero-balance credit account.

**Napises v CRM:** kopiruj pattern pro svoje onboarding projekce: public event handler bere jen contract event + `IDispatcher`, vsechna logika zustava v internal commandu.

**EC:**

- EC231 duplicate event → UNIQUE `CreditAccount.UserId` + handler no-op zajisti presne jeden ucet.
- EC232 worker retry → opakovane zavolani public handleru je idempotentni a neprepise existujici balance.
- EC233 account already exists → `EnsureCreditAccountCommand` nic nemeni, kdyz uz ucet existuje.
- EC234 multiple handlers on same event → stejny registration event obslouzi Billing i Notifications; kazdy subscriber musi byt idempotentni.
- EC235 public handler dispatchuje internal command → `ProvisionCreditAccountHandler` je public shell pro Wolverine, business logika je v internal `EnsureCreditAccountCommand`.

## Notifications

### UC48 Poslat notifikaci

**Status:** Implemented + tested — `NotificationsIntegrationTests`.

**Pouzijes:** `POST /notifications/send` nebo `SendNotificationCommand`.

**Co se stane:** Notifications vyrenderuje template, ulozi in-app feed row a email/push prida do Wolverine outboxu.

**Napises v CRM:** command/event s recipientem, template key, daty a `idempotencyKey`. Pres HTTP posilej jen opravneny/admin caller; bezny user si nema posilat notifikace cizim userum.

**EC:**

- EC236 missing template → handler vraci 404 `notification.template_not_found`; welcome handler tuhle chybu bere jako non-fatal.
- EC237 invalid channel → validator povoli jen `email`, `push`, `inapp`, jinak `notification.channel.invalid`.
- EC238 duplicate send retry → `IdempotencyKey` ma UNIQUE index; opakovany keyed send vrati OK a nevytvori druhou row/email/push.
- EC239 recipient z tokenu nebo validni target → HTTP endpoint vyzaduje `notifications.send`; RLS dovoli row jen pro vlastniho usera, cross-user send dela worker/system command.
- EC240 publish realtime az po commitu → email/push jsou v transakcnim outboxu, realtime `notification` event se publikuje az po uspesnem `SaveChangesAndFlushMessagesAsync`.

### UC49 Zobrazit moje notifikace

**Status:** Implemented + tested — `NotificationsIntegrationTests`, `GdprIntegrationTests`.

**Pouzijes:** `GET /notifications/me`.

**Co se stane:** User vidi owner-scoped, paged feed z `notifications`, newest first, volitelne jen unread.

**Napises v CRM:** nic specialniho; FE pouzije platform endpoint, invaliduje/refetchuje feed po mark-read nebo po realtime eventu.

**EC:**

- EC241 paging → `page/pageSize` jde pres `PageRequest`, odpoved je `PagedResponse`.
- EC242 unread filter → `unreadOnly=true` filtruje `ReadAt == null`.
- EC243 foreign notification hidden → query bere `UserId` z tokenu a RLS drzi owner scope; cizi notifikace se ve feedu neobjevi.
- EC244 erased PII v title/body → GDPR erasure nechava row, ale blankuje `Title`/`Body`; encrypted live columns po shredu nevraci plaintext.
- EC245 stale cache po mark-read → po mark-read FE invaliduje feed/unread-count; pri ztracenem SSE ma fallback refetch.

### UC50 Unread count

**Status:** Implemented + tested — `NotificationsIntegrationTests`.

**Pouzijes:** `GET /notifications/me/unread-count`.

**Co se stane:** Frontend dostane owner-scoped pocet `ReadAt == null` notifikaci.

**Napises v CRM:** jen badge v navigaci; nedrz druhy counter v CRM DB, vzdy refetchuj platform count.

**EC:**

- EC246 count stale po mark-read → po `mark-read`/`mark-all-read` invaliduj unread-count query; endpoint po read vraci nizsi count.
- EC247 SSE event lost -> refetch fallback → realtime je UX hint, pravda je `GET /notifications/me/unread-count`.
- EC248 count owner-scoped → query bere usera z tokenu; cizi notifikace count nezvysi.
- EC249 loading skeleton → FE ukaze badge skeleton/placeholder, neukazuje stary count jako pravdu behem refetch.
- EC250 neudrzovat druhy unread counter v CRM → CRM si count neuklada, maximum cache s invalidaci.

### UC51 Mark notification read

**Status:** Implemented + tested — `NotificationsIntegrationTests`.

**Pouzijes:** `POST /notifications/{notificationId}/read`.

**Co se stane:** Notifications najde notifikaci podle `notificationId` a `UserId` z tokenu, a pokud je unread, nastavi `ReadAt`.

**Napises v CRM:** mutation invaliduje list a unread count; optimistic update je OK, ale pri erroru rollback.

**EC:**

- EC251 foreign id -> 404 → handler filtruje `Id && UserId`, cizi id vypada jako `notification.not_found`.
- EC252 already read -> idempotent/no-op → pokud `ReadAt` uz je vyplnene, endpoint vrati OK a nic nemeni.
- EC253 double click → opakovany POST na stejne id zustane OK a nevytvori druhy side effect.
- EC254 stale feed → FE po OK invaliduje `/notifications/me` i `/unread-count`.
- EC255 optimistic update rollback pri erroru → pri 404/401 vratis polozku v UI zpet do unread stavu.

### UC52 Mark all read

**Status:** Implemented + tested — `NotificationsIntegrationTests`.

**Pouzijes:** `POST /notifications/me/read-all`.

**Co se stane:** Notifications atomicky nastavi `ReadAt` na vsech mych unread notifikacich.

**Napises v CRM:** button disable behem pending, po OK invaliduj feed i unread-count.

**EC:**

- EC256 empty feed → endpoint vrati OK a `marked=0`.
- EC257 concurrent new notification → bulk update oznaci jen radky splnujici `ReadAt == null` v okamziku update; nova pozdejsi notifikace zustane unread.
- EC258 stale unread count → po OK refetch unread-count, jinak badge zustane stary.
- EC259 bulk update audit caveat podle implementace → `ExecuteUpdate` bypassuje audit/xmin; je to zamerne pro read-flag flip.
- EC260 retry nesmi rozbit stav → druhy POST je no-op s `marked=0`.

### UC53 Welcome notification

**Status:** Implemented + tested — `NotificationsIntegrationTests`.

**Pouzijes:** `SendWelcomeHandler` na `UserRegisteredIntegrationEvent`.

**Co se stane:** Worker zachyti `UserRegisteredIntegrationEvent` a pres `SendNotificationCommand` posle welcome email + in-app.

**Napises v CRM:** podobny handler pro CRM onboarding jen pokud je potreba; handler musi byt public shell a dispatchovat existujici command.

**EC:**

- EC261 missing welcome template → handler chyti `NotFoundException`, zaloguje warning a nedeadletteruje registration event.
- EC262 duplicate event → `IdempotencyKey = welcome:{userId}` zajisti jednu welcome row.
- EC263 handler retry → opakovany handler call je no-op diky idempotency key.
- EC264 user erased before delivery → `SendNotificationCommand`/PII protector nesmi obnovit shredded DEK; GDPR eraser blankuje existujici rows.
- EC265 handler musi byt explicitne registrovany → `NotificationsModule.ConfigureMessaging` vola `IncludeType<SendWelcomeHandler>()`.

### UC54 Purchase completed notification

**Status:** Implemented + tested — `NotificationsIntegrationTests.CreditPurchaseCompleted_event_creates_purchase_completed_notification` a `Purchase_completed_handler_is_idempotent_and_missing_template_is_non_fatal`.

**Pouzijes:** Notifications handler na `CreditPurchaseCompletedIntegrationEvent`.

**Co se stane:** Billing po uspesnem nakupu publikuje `CreditPurchaseCompletedIntegrationEvent`. Notifications ma public Wolverine handler `SendPurchaseCompletedHandler`, ktery nevezme zadnou Billing tabulku, ale jen payload eventu (`UserId`, `PurchaseId`, `CreditAmount`). Handler zavola existujici slice `SendNotificationCommand` s template `purchase_completed`, channel `inapp` a `IdempotencyKey = purchase-completed:{purchaseId}`. Tim se pouzije normalni Notifications flow: render template, ulozit in-app row, pripadne predat email/push pres outbox.

**Napises v CRM:** kdyz CRM dokonci obchod nebo AI beh, nevkladej notifikaci primo. CRM publikuje treba `DealWonIntegrationEvent` nebo `AiRunCompletedEvent`. V Notifications napises handler `SendDealWonHandler`, ktery z eventu posklada `data` pro template a zavola `SendNotificationCommand(..., IdempotencyKey: $"deal-won:{dealId:N}")`. Dulezite je, ze CRM zustane vlastnik obchodu a Notifications zustane vlastnik doruceni.

**Vzor kodu:**

```csharp
public sealed class SendDealWonHandler(ILogger<SendDealWonHandler> logger)
{
    public async Task Handle(DealWonIntegrationEvent message, IDispatcher dispatcher, CancellationToken ct)
    {
        try
        {
            await dispatcher.Send(new SendNotificationCommand(
                message.UserId,
                "deal_won",
                ["inapp"],
                new Dictionary<string, string>
                {
                    ["locale"] = "en",
                    ["dealName"] = message.DealName,
                },
                IdempotencyKey: $"deal-won:{message.DealId:N}"), ct);
        }
        catch (NotFoundException)
        {
            logger.LogWarning("Deal-won notification skipped: template missing for {DealId}.", message.DealId);
        }
    }
}
```

**Nepouzijes:** zadny direct insert do `notifications`, zadny call do Billing Core, zadny vlastni retry loop. Retry dela Wolverine, dedup dela `SendNotificationCommand.IdempotencyKey`.

**EC:**

- EC266 Billing event duplicate → dva eventy se stejnym `PurchaseId` maji stejny `IdempotencyKey`, tak vznikne jen jedna notifikace.
- EC267 notifikace musi mit idempotency key → key je `purchase-completed:{purchaseId:N}` a patri do `SendNotificationCommand`, ne do rucni tabulky.
- EC268 purchase event order → handler necte aktualni Billing stav a nespoléha na poradi eventu; bere event jako hotovy fakt.
- EC269 missing template → `NotFoundException` se zachyti a zaloguje; jeden chybejici seed nesmi otravit Wolverine inbox.
- EC270 retry nesmi poslat dva emaily → duplicitni send rollbackne na UNIQUE idempotency key jeste pred outbox handoffem, tak stejny retry nevytvori dalsi feed row ani dalsi delivery message.

### UC55 Email delivery

**Status:** Implemented + tested — `ChannelDeliveryHandlersTests` + existing `SendNotification_persists_an_inapp_row_and_enqueues_channel_delivery_via_the_outbox`.

**Pouzijes:** `EmailDeliveryHandler`.

**Co se stane:** `SendNotificationHandler` pri channel `email` nevykona SMTP v HTTP requestu. Jen publikuje `EmailDeliveryRequested` do Wolverine outboxu. Worker potom spusti `EmailDeliveryHandler`, ten vezme uz vyrenderovany `ToAddress`, `Subject`, `Body` a zavola `IEmailSender.SendAsync(...)`. Kdyz sender hodi exception, handler ji nechyta, aby Wolverine mohl udelat retry a pripadne DLQ.

**Napises v CRM:** nic specialniho. CRM nikdy neposila email samo. CRM zavola notification slice/event a jen rekne `channels = ["email"]` nebo `["email", "inapp"]`. Email delivery patri Notifications modulu.

**Vzor kodu:**

```csharp
await dispatcher.Send(new SendNotificationCommand(
    userId,
    "deal_assigned",
    ["email", "inapp"],
    new Dictionary<string, string>
    {
        ["email"] = assigneeEmail,
        ["dealName"] = dealName,
    },
    IdempotencyKey: $"deal-assigned:{dealId:N}:{assigneeUserId:N}"), ct);
```

**Co si pohlidas:** email adresa jde do `data["email"]`, subject/body se berou z template, a PII v body zustava v durable envelope jen po omezenou dobu podle Wolverine retention. Pokud SMTP spadne, nesmis delat `try/catch` a oznacit to za hotove; exception musi spadnout do Wolverine retry/DLQ.

**Nepouzijes:** `SmtpClient` primo v CRM handleru, zadny HTTP request cekajici na SMTP, zadny custom retry loop.

**EC:**

- EC271 SMTP down → `IEmailSender.SendAsync` hodi exception; handler ji pusti ven.
- EC272 transient retry → retry dela Wolverine durable messaging, proto handler nema vlastni catch ani loop.
- EC273 permanent bounce → nepatri do CRM. Provider adapter/sender ji vyhodi jako delivery failure; po retriech skonci v DLQ nebo se pozdeji doplni specialni bounce event podle provideru.
- EC274 PII v email body → PII je v `EmailDeliveryRequested.Body`; platforma ma bounded durable-envelope retention (`KeepAfterMessageHandling`, DLQ expirace). Nedavej tam vic osobnich dat, nez email potrebuje.
- EC275 DLQ monitoring → sleduje `MessagingHealthJob` pres `platform.messaging.dead_letters`; modul nema vlastni dead-letter tabulku.

### UC56 Push delivery

**Status:** Implemented + tested — `ChannelDeliveryHandlersTests`.

**Pouzijes:** `PushDeliveryHandler`.

**Co se stane:** `SendNotificationHandler` pri channel `push` publikuje `PushDeliveryRequested` do outboxu. Worker spusti `PushDeliveryHandler`, ktery zavola `IPushSender.SendAsync(userId, title, body)`. Dnesni provider je `NoOpPushSender`, takze base umi cely durable pipeline bez externiho FCM/Expo uctu. Realny provider se pozdeji vymeni za implementaci `IPushSender`.

**Napises v CRM:** nic navic. CRM jen pozada o notification s `channels = ["push"]` nebo `["push", "inapp"]`. CRM nema znat device tokeny ani FCM/Expo.

**Vzor kodu:**

```csharp
await dispatcher.Send(new SendNotificationCommand(
    userId,
    "deal_due",
    ["push", "inapp"],
    new Dictionary<string, string>
    {
        ["dealName"] = dealName,
    },
    IdempotencyKey: $"deal-due:{dealId:N}"), ct);
```

**Co si pohlidas:** push je best-effort delivery channel. Trvaly fakt musi zustat v CRM nebo v in-app notification/feedu. Push samotny nesmi byt jedine misto, kde user zjisti dulezitou vec.

**Nepouzijes:** zadny vlastni push client v CRM, zadne device tokeny v CRM tabulkach, zadny fire-and-forget provider call z HTTP requestu.

**EC:**

- EC276 push provider no-op/dev → base registruje `NoOpPushSender`, takze se pipeline da vyvijet a testovat bez provideru.
- EC277 missing device token → dnes neni token registry v base; realny `IPushSender` to ma resit jako no-op pro daneho usera, ne jako CRM chybu.
- EC278 provider failure → provider exception se nechyta v handleru; Wolverine retry/DLQ rozhodne, co dal.
- EC279 duplicate push retry → pro dulezite zpravy pridej `IdempotencyKey` uz na `SendNotificationCommand`; bez toho je push channel at-least-once.
- EC280 user revoked notifications consent → consent/token filtering patri do budouciho `IPushSender` nebo Notifications preferenci, ne do CRM modulu.

## Files

### UC57 Upload souboru

**Status:** Implemented + tested — `FilesUploadTests` pokryva roundtrip, allowlist, size cap, owner scope a server-generated storage key.

**Pouzijes:** `POST /files`.

**Co se stane:** Frontend posle multipart `file` na `POST /files`. Endpoint vezme usera z tokenu, otevre stream a posle `UploadFileCommand`. Handler vygeneruje nove `fileId`, z nej serverovy `StorageKey = {userId:N}/{fileId:N}`, ulozi bytes pres `IFileStorage.PutAsync(...)` a potom ulozi metadata do `file_objects`. Response vrati `id`, `fileName`, `contentType`, `size` a `Location: /v1/files/{id}`.

**Napises v CRM:** po uploadu neukladas bytes ani storage key. CRM si ulozi jen `FileObjectId` do vlastni join entity, napr. `DealAttachment(DealId, FileObjectId, UploadedByUserId)`. Kdyz user klikne download, CRM pouzije `/files/{fileId}` nebo vlastni endpoint, ktery si overi CRM permission a pak odkazuje na Files.

**Vzor frontendu:**

```ts
const body = new FormData();
body.append("file", file);

await api.post("/v1/files", body);
```

**Vzor CRM vazby:**

```csharp
await dispatcher.Send(new AttachFileToDealCommand(
    dealId,
    uploadResponse.Id,
    tenant.UserId!.Value), ct);
```

**Co si pohlidas:** frontend validace velikosti/typu je jen UX. Backend validator je autorita. `file.FileName` je jen display name, nikdy storage path.

**Nepouzijes:** zadny direct write do S3/local disk z CRM, zadny storage key z client filename, zadne cross-module join na `file_objects`.

**EC:**

- EC281 file > 10 MB → endpoint ma request-size limit a validator vrati 400/413; metadata se neulozi.
- EC282 disallowed content type → `FileUploadPolicy.AllowedContentTypes` je deny-by-default allowlist; backend odmita `application/x-msdownload`.
- EC283 client filename nesmi byt storage key → storage key je `{userId:N}/{fileId:N}`; test uploaduje `../crm/contracts/q4.txt` a key neobsahuje filename ani `..`.
- EC284 blob upload uspeje, metadata failne -> cleanup/reconcile gap → handler po `SaveChanges` failure dela best-effort `storage.DeleteAsync(storageKey)` a loguje cleanup error. Pokud cleanup taky selze, zustane orphan blob; to je future reconcile job, ne CRM logika.
- EC285 frontend validace je jen UX, backend je autorita → i kdyz frontend nezkontroluje typ/velikost, validator a request cap to zastavi server-side.

### UC58 List moje soubory

**Status:** Implemented + tested — `FilesUploadTests.List_is_paged_and_owner_scoped` a `List_search_filters_by_filename_and_deleted_files_disappear`.

**Pouzijes:** `GET /files`.

**Co se stane:** Files vrati metadata souboru pro prihlaseneho usera: `id`, `fileName`, `contentType`, `size`, `createdAt`. Handler pouziva read DbContext, filtruje `UserId == token user`, radi nejnovsi prvni a vraci `PagedResponse`. Kdyz posles `search`, filtruje se case-insensitive podle `FileName`; bytes se nikdy necitaji.

**Napises v CRM:** CRM si udela vlastni list attachmentu z CRM tabulky, napr. `DealAttachment`, kde ma `DealId` a `FileObjectId`. Files `GET /files` je obecny "moje soubory" list, ne CRM attachment list. Pro deal detail nejdriv over CRM permission na deal a az potom zobraz file ids prirazene k dealu.

**Vzor frontendu:**

```ts
const files = await api.get("/v1/files", {
  params: { page: 1, pageSize: 20, search: query || undefined },
});
```

**Vzor CRM query:**

```csharp
var attachments = await db.DealAttachments
    .Where(x => x.DealId == dealId && x.UserId == tenant.UserId)
    .OrderByDescending(x => x.CreatedAt)
    .ToListAsync(ct);
```

**Co si pohlidas:** `GET /files` neni admin endpoint. Neexistuje parametr `userId`. Kdyz CRM potrebuje "soubory na dealu", patri to do CRM modelu jako vazba na `FileObjectId`.

**Nepouzijes:** cross-module JOIN na `file_objects`, route/body user id, ani frontend filtrovani jako security.

**EC:**

- EC286 paging → `PageRequest`/`PagedResponse`, testuje se `pageSize=2`.
- EC287 search/filter stale → search jde na server pres `ILIKE`, po zmene/delete je potreba invalidovat frontend cache.
- EC288 owner scope → handler ma explicitni `WHERE UserId == query.UserId` a tabulka je `IUserOwned`/RLS.
- EC289 deleted files → po `DELETE /files/{id}` metadata zmizi a list/search uz soubor nevrati.
- EC290 CRM nema listovat cizi user files → CRM listuje svoje join rows a Files download/list jeste samostatne overi owner scope.

### UC59 Download souboru

**Status:** Implemented + tested — `FilesUploadTests.Upload_then_download_round_trips_the_same_bytes_and_content_type`, `A_different_user_cannot_download_another_users_file`, `Download_returns_404_when_metadata_exists_but_blob_is_missing` a `StorageUnitTests`.

**Pouzijes:** `GET /files/{fileId}`.

**Co se stane:** Download endpoint vezme `fileId` z route a usera z tokenu. `GetFileQuery(fileId, userId)` vrati jen metadata vlastnene tim userem: `StorageKey`, display `FileName`, `ContentType`. Endpoint potom otevre stream pres `IFileStorage.GetAsync(storageKey)` a vrati `Results.Stream(...)` s ulozenym content type a file name. Response neni `ApiResponse`, je to realny stream.

**Napises v CRM:** v detailu dealu zobrazis attachment button/link s `FileObjectId`. Pred zobrazenim overis CRM permission na deal. Samotny Files endpoint jeste znovu overi, ze file patri prihlasenemu userovi.

**Vzor frontendu:**

```ts
window.open(`/v1/files/${fileObjectId}`, "_blank");
```

**Vzor CRM endpointu, kdyz chces vlastni routing:**

```csharp
// CRM overi, ze user vidi deal a ten ma prirazeny FileObjectId.
// Bytes ale porad streamuje Files endpoint/building block, ne CRM storage klient.
```

**Co si pohlidas:** storage key nikdy neposilas na frontend. Frontend zna jen `fileId`. Kdyz blob chybi, provider vrati `file.not_found`, ne 500.

**Nepouzijes:** primy S3/local path download z CRM, client-provided storage key, ani rozbaleni celeho streamu do pameti.

**EC:**

- EC291 foreign file id -> 404 → `GetFileHandler` ma explicitni `WHERE Id == fileId && UserId == tokenUser`; cizi id vypada jako neexistujici.
- EC292 blob missing → metadata muze existovat, ale provider `GetAsync` vrati `file.not_found`; test maze blob a overuje 404.
- EC293 content type → stream response pouzije ulozeny `ContentType`, test roundtrip overuje `text/plain`.
- EC294 large stream failure → endpoint streamuje `Stream`, necte bytes cele do RAM; storage/provider exception propadne jako failure.
- EC295 storage key traversal guard → `StorageKey.Validate` odmita `..`, absolute path, backslash a invalid znaky; unit testy to drzi.

### UC60 Rename souboru

**Status:** Implemented + tested — `FilesUploadTests.Rename_updates_display_name_only_and_keeps_storage_key_unchanged` a `Rename_validates_file_name_and_keeps_foreign_ids_hidden`.

**Pouzijes:** `PATCH /files/{fileId}`.

**Co se stane:** Endpoint vezme `fileId` z route, `fileName` z body a usera z tokenu. `RenameFileHandler` najde soubor jen kdyz patri userovi, zmeni pouze `FileName` v metadata row a ulozi EF tracked entity. Blob ani `StorageKey` se nemení.

**Napises v CRM:** pokud CRM zobrazuje attachmenty, po rename invaliduj CRM attachment list i obecny Files list. CRM nemeni `file_objects` napriamo; maximalne zavola Files endpoint a potom obnovi svoje view.

**Vzor frontendu:**

```ts
await api.patch(`/v1/files/${fileId}`, { fileName: nextName });
queryClient.invalidateQueries({ queryKey: ["files"] });
queryClient.invalidateQueries({ queryKey: ["deal", dealId, "attachments"] });
```

**Vzor CRM:** kdyz chces rename z deal detailu, nejdriv over, ze user vidi deal a attachment patri k dealu. Potom zavolej `PATCH /files/{fileId}`. Neobchazej Files modul.

**Co si pohlidas:** rename je display metadata, ne presun blobu. Kdyz user posle stejny nazev znovu, je to bezpecna idempotentni aktualizace.

**Nepouzijes:** update `StorageKey`, direct update do `file_objects` z CRM, ani route/body user id.

**EC:**

- EC296 empty filename → validator vraci `file.name.required`.
- EC297 too long filename → limit 512 znaku, validator vraci `file.name.too_long`.
- EC298 foreign id → handler hleda `Id && UserId`; cizi id je 404.
- EC299 concurrent rename → jde o tracked EF write, tak funguje platform `xmin`/`ConcurrencyRetryBehavior`; posledni uspesny rename vyhraje, bez zmeny storage key.
- EC300 CRM nema prepisovat metadata napriamo → CRM vola Files endpoint/slice, proto zustane audit, owner scope a validace na jednom miste.

### UC61 Delete souboru

**Status:** Implemented + tested — `FilesUploadTests.Delete_is_owner_scoped_removes_metadata_and_second_delete_is_404` + delete visibility in list tests.

**Pouzijes:** `DELETE /files/{fileId}`.

**Co se stane:** Endpoint vezme `fileId` z route a usera z tokenu. `DeleteFileHandler` najde jen soubor vlastneny userem. Nejdřív smaze blob pres `IFileStorage.DeleteAsync(storageKey)`, potom smaze metadata row `file_objects`. Kdyz blob delete selze, exception propadne ven a metadata zustanou, aby se dal problem dohledat a opravit.

**Napises v CRM:** kdyz user odstrani soubor z dealu, vetsinou nema CRM hned mazat Files blob, ale odstranit/deaktivovat vazbu `DealAttachment`. Fyzicky `DELETE /files/{fileId}` volej jen kdyz ma user opravdu mazat svuj soubor z Files. Po uspesnem delete invaliduj CRM attachment list i `GET /files`.

**Vzor frontendu:**

```ts
await api.delete(`/v1/files/${fileId}`);
queryClient.invalidateQueries({ queryKey: ["files"] });
queryClient.invalidateQueries({ queryKey: ["deal", dealId, "attachments"] });
```

**Vzor CRM:** `RemoveDealAttachmentCommand` ma smazat vazbu. `DeleteFileCommand` pouzij jen pro product akci "smazat soubor", ne pro "odebrat z dealu".

**Co si pohlidas:** delete neni global admin delete. Cizi `fileId` je 404. Druhe smazani je taky 404, proto UI musi po uspesnem delete refreshnout cache.

**Nepouzijes:** mazani blobu primym storage key z CRM, ponechani stale attachment row bez cleanupu, ani optimistic UI bez rollbacku.

**EC:**

- EC301 foreign id -> 404 → handler filtruje `Id && UserId`; cizi id se tvari jako neexistujici.
- EC302 already deleted → druhe `DELETE` vrati 404, proto frontend po prvnim 204 musi vyhodit item z cache.
- EC303 blob delete fails → handler loguje a vyhodi provider exception; metadata row zustane jako dohledatelny problem.
- EC304 CRM vazba zustane orphan -> cleanup → CRM ma vlastni vazbu deaktivovat/smazat, idealne v jedne CRM command transakci; Files nema znat CRM tabulky.
- EC305 UI stale list → invaliduj `files` i CRM attachment query po 204.

### UC62 Pripojit file k CRM entite

**Status:** Blueprint for CRM module — base nema CRM modul, implementuje se v novem CRM Core podle tohoto vzoru.

**Pouzijes:** Files upload + CRM join table.

**Co se stane:** Files modul vlastni blob a metadata souboru. CRM modul vlastni jen vazbu mezi CRM entitou a file id, napr. `DealAttachment(DealId, FileObjectId, UserId, CreatedAt)`. Upload probiha pres `POST /files`, response vrati `FileObjectId`. CRM potom zavola `AttachFileToDealCommand`, ktery overi, ze deal patri userovi/tenantovi, a ulozi vazbu. CRM nikdy necita `file_objects` tabulku primo.

**Napises v CRM:** command `AttachFileToDealCommand` + validator + endpoint. Entity ma unique index `(DealId, FileObjectId)`, aby double-click nevytvoril duplicitni attachment.

**Vzor entity:**

```csharp
internal sealed class DealAttachment : AuditableEntity, IUserOwned
{
    public Guid DealId { get; set; }
    public Guid FileObjectId { get; set; }
    public Guid UserId { get; set; }
}
```

**Vzor commandu:**

```csharp
public sealed record AttachFileToDealCommand(
    Guid DealId,
    Guid FileObjectId,
    Guid UserId) : ICommand<DealAttachmentResponse>;
```

**Vzor handleru:**

```csharp
var deal = await db.Deals
    .Where(x => x.Id == command.DealId && x.UserId == command.UserId)
    .FirstOrDefaultAsync(ct)
    ?? throw new NotFoundException("crm.deal_not_found", "Deal not found.");

db.DealAttachments.Add(new DealAttachment
{
    DealId = deal.Id,
    FileObjectId = command.FileObjectId,
    UserId = command.UserId,
});

try
{
    await db.SaveChangesAsync(ct);
}
catch (DbUpdateException ex) when (IsUniqueViolation(ex))
{
    // Idempotent double-click: return existing attachment.
}
```

**Jak overis file access:** v prvni verzi CRM ulozi jen id a spoleha na Files endpoint, ktery pri downloadu znovu overi owner scope. Pokud potrebujes overit uz pri attachi, nepristupuj na Files Core ani DB; pouzij verejny endpoint/query kontrakt, pokud pro to v base vznikne. Do te doby attach autorizuj pres deal ownership a user-owned file download zustava final guard.

**Frontend flow:**

```ts
const uploaded = await uploadFile(file);
await api.post(`/v1/crm/deals/${dealId}/attachments`, {
  fileObjectId: uploaded.id,
});
invalidateDealAttachments(dealId);
```

**Nepouzijes:** navigation property na FileObject, cross-module JOIN, storage key v CRM, ani `fileUserId` z request body.

**EC:**

- EC306 deal foreign user -> 404 → command hleda deal pres `DealId && UserId/TenantId`; cizi deal se tvari jako neexistujici.
- EC307 file foreign user -> 404 → finalni guard je Files download/list owner scope; pro eager validation nepristupuj na Files DB, dopln verejny Files query/endpoint.
- EC308 duplicate attachment → unique index `(DealId, FileObjectId)` + catch unique violation = idempotentni double-click.
- EC309 deal deleted → attach command vrati `crm.deal_not_found`; list attachmentu ma filtrovat jen existujici/aktivni deal.
- EC310 GDPR erase musi odstranit i vazby nebo anonymizovat metadata → CRM `IErasePersonalData` smaze/anonymizuje `DealAttachment` rows pro subjecta; Files eraser smaze samotne soubory subjecta.

## Operations, Worker, Jobs, Realtime

### UC63 Start dlouhe operace

**Status:** Implemented pattern + tested — `OperationsTests.Demo_operation_is_accepted_runs_on_the_worker_and_is_owner_scoped`.

**Pouzijes:** Operations pattern `POST /operations/demo` nebo vlastni CRM run table.

**Co se stane:** HTTP request nesmi delat dlouhou praci. Accept handler vytvori `Operation` se stavem `Pending`, publikuje durable work message pres Wolverine outbox a commitne oboji pres `SaveChangesAndFlushMessagesAsync()`. Endpoint vrati `202 Accepted`, `operationId` a `Location` na status endpoint. Worker potom udela praci a prepne stav na `Running`/`Succeeded`/`Failed`.

**Napises v CRM:** `StartCrmImportCommand` nebo `StartCrmAiRunCommand`. Bud pouzijes `IOperationStore`, pokud ti staci obecny operation status, nebo vlastni CRM run tabulku, pokud potrebujes domenove stavy/import statistiky.

**Vzor accept handleru:**

```csharp
internal sealed class StartCrmImportHandler(
    IDbContextOutbox<CrmDbContext> outbox,
    IOperationStore operations)
    : ICommandHandler<StartCrmImportCommand, StartCrmImportResponse>
{
    public async Task<StartCrmImportResponse> Handle(StartCrmImportCommand command, CancellationToken ct)
    {
        var operationId = await operations.CreateAsync("crm-import", command.UserId, ct);

        await outbox.PublishAsync(new RunCrmImport(operationId, command.ImportId));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return new StartCrmImportResponse(operationId);
    }
}
```

**Vzor endpointu:** vracej `202 Accepted`, ne `200 OK`, a `Location` sklad pres named route/status endpoint, ne string hardcode.

**Co si pohlidas:** worker message musi byt idempotentni. Kdyz user klikne dvakrat, bud vytvoris dva nezavisle runy, nebo pouzijes idempotency key podle business akce.

**Nepouzijes:** `Task.Run`, dlouhy HTTP request, fire-and-forget bez outboxu, ani frontend timer jako source of truth.

**EC:**

- EC311 request nesmi cekat na dlouhou praci → endpoint jen zalozi stav a vrati 202.
- EC312 operation musi byt owner-scoped → `Operation` je `IUserOwned`, status query filtruje `UserId`.
- EC313 duplicate click → podle produktu bud dovol dva runy, nebo pridej idempotency key v CRM accept commandu.
- EC314 outbox publish a DB save atomicky → pouzij `IDbContextOutbox.SaveChangesAndFlushMessagesAsync()`.
- EC315 response musi obsahovat status link/id → body ma `operationId`, header `Location` vede na `GET /operations/{id}` nebo CRM status.

### UC64 Get operation status

**Status:** Implemented pattern + tested — `OperationsTests.Demo_operation_is_accepted_runs_on_the_worker_and_is_owner_scoped`, `Operation_status_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed` a `A_terminal_operation_is_not_resurrected_by_a_duplicate_worker_transition`.

**Pouzijes:** `GET /operations/{operationId}` nebo CRM status endpoint.

**Co se stane:** Frontend polluje status endpoint. Handler cte jen operation vlastnenou token userem a vraci `Id`, `Type`, `Status`, `ResultJson`, `ErrorCode`, `ErrorDetail`, `CompletedAt`. `Pending/Running` znamena "polluj dal", `Succeeded/Failed` je terminal state.

**Napises v CRM:** pokud pouzijes obecny Operations modul, frontend vola `/operations/{operationId}`. Pokud potrebujes domenovy detail, udelej CRM endpoint `GET /crm/imports/{id}` nebo `GET /crm/runs/{id}`, ale drz stejny tvar: stav, result/error, completedAt.

**Vzor frontendu polling:**

```ts
const status = await api.get(`/v1/operations/${operationId}`);
if (status.data.status === "Pending" || status.data.status === "Running") {
  scheduleNextPoll({ backoff: true });
}
```

**Vzor CRM DTO:**

```csharp
public sealed record CrmRunStatusResponse(
    Guid Id,
    string Status,
    string? ErrorCode,
    string? ErrorDetail,
    DateTimeOffset? CompletedAt);
```

**Co si pohlidas:** status endpoint nespousti praci znovu. Je to read-only query. Pokud worker spadne, status muze zustat `Running`; to resi UC67/UC68 monitoring/reconcile, ne frontend.

**Nepouzijes:** global operation lookup bez `UserId`, frontend-only status state, ani polling bez stop podminky.

**EC:**

- EC316 foreign operation -> 404 → handler filtruje `OperationId && UserId`, cizi id je `operation.not_found`.
- EC317 operation not found → stejny 404/error code, zadne info leak.
- EC318 stuck processing → status muze zustat `Running`; monitoring/reconcile musi najit stuck run, UI ukaze pending/timeout stav.
- EC319 terminal state guard → `IOperationStore` neprepisuje `Succeeded/Failed` zpet na `Running`.
- EC320 frontend polling backoff → pouzij interval/backoff a zastav polling na terminal state.

### UC65 List moje operace

**Status:** Implemented + tested — `OperationsTests.Operations_list_is_paged_owner_scoped_newest_first_and_has_empty_state`.

**Pouzijes:** `GET /operations`.

**Co se stane:** User vidi historii svych dlouhych tasku. Handler vraci jen jeho `operations`, radi nejnovsi prvni, strankuje pres `PageRequest` a vraci `PagedResponse<OperationListItem>`. List nevraci `ResultJson`, jen summary pro UI: id, type, status, errorCode, completedAt, createdAt.

**Napises v CRM:** pokud ti staci obecne operace, zobraz `GET /operations`. Pokud potrebujes domenu detailneji, udelej vlastni list `CrmRuns` s import statistykou, poctem radku, nazvem souboru atd.

**Vzor frontendu:**

```ts
const runs = await api.get("/v1/operations", {
  params: { page: 1, pageSize: 20 },
});
```

**Vzor CRM list item:**

```csharp
public sealed record CrmRunListItem(
    Guid Id,
    string Status,
    int ProcessedRows,
    int FailedRows,
    DateTimeOffset CreatedAt);
```

**Co si pohlidas:** empty state je normalni stav. List je owner-scoped; nikdy neposilej `userId` query param. Po startu operace invaliduj list, aby se novy `Pending` run objevil hned.

**Nepouzijes:** globální operations list v tenant UI, frontend filtr jako security, ani list bez paging.

**EC:**

- EC321 paging → `page/pageSize`, testuje se `pageSize=2`.
- EC322 old operations retention → base zatim nema purge policy; pokud historie roste, pridej per-module retention rozhodnuti.
- EC323 owner scope → handler filtruje `UserId`; cizi user ma prazdny list.
- EC324 sort order → newest first podle `CreatedAt`.
- EC325 empty state → prazdny list vraci `items=[]`, `totalCount=0`, ne error.

### UC66 Worker dokonci praci

**Status:** Implemented pattern + tested — `OperationWorkerFailureTests` a `OperationsTests.A_terminal_operation_is_not_resurrected_by_a_duplicate_worker_transition`.

**Pouzijes:** Wolverine handler.

**Co se stane:** Worker handler dostane durable message, prepne operation na `Running`, provede skutecnou praci a pak zavola `CompleteAsync`. Kdyz prace spadne, chyti exception, zaloguje ji a zavola `FailAsync` s generickym user-facing errorem. Pokud nejde zapsat ani fail stav, exception propadne ven a Wolverine zpravu retryne / presune do DLQ.

**Napises v CRM:** public Wolverine handler + internal command/sluzbu pro domenu. Handler ma byt tenka public shell, protoze Wolverine scanuje public typy. Vlastni business prace patri do CRM commandu nebo domain metody, ne do endpointu.

**Vzor workeru:**

```csharp
public sealed class RunCrmImportHandler
{
    public async Task Handle(RunCrmImport message, IOperationStore operations, IDispatcher dispatcher, CancellationToken ct)
    {
        try
        {
            await operations.MarkRunningAsync(message.OperationId, ct);
            var result = await dispatcher.Send(new ExecuteCrmImportCommand(message.ImportId), ct);
            await operations.CompleteAsync(message.OperationId, result, ct);
        }
        catch
        {
            await operations.FailAsync(message.OperationId, "crm.import_failed", "Import failed.", ct);
        }
    }
}
```

**Co si pohlidas:** handler musi byt idempotentni/order-independent. Kdyz externi call probehl, ale save spadl, retry muze zavolat externi system znovu; pro takove integrace pouzij idempotency key/reconciliation.

**Nepouzijes:** swallow exception bez terminal state, raw exception message do user detailu, ani worker business logiku bez testu na retry/failure.

**EC:**

- EC326 message retry → pokud `FailAsync`/commit selze, exception propadne ven a Wolverine retryne.
- EC327 duplicate message → `IOperationStore` ignoruje prechod terminal state zpet na Running.
- EC328 worker crash between external call and save → navrhni externi call idempotentne a pridej reconcile; jinak retry muze zopakovat side effect.
- EC329 terminal state no-op → `Succeeded/Failed` jsou finalni, test hlida duplicate transition.
- EC330 DLQ needs support/reconcile story → DLQ sleduje `MessagingHealthJob`; domenove stuck runy resi CRM reconcile job.

### UC67 Cron job

**Status:** Implemented pattern — canonical examples `BillingStripeReconcileJob`, `BillingExpireCreditsJob`, `GdprRetentionSweepJob`; non-overlap overeno v Billing tests.

**Pouzijes:** `IModule.RegisterJobs` + Quartz.

**Co se stane:** Jobs host pri startu zavola `RegisterJobs` na kazdem enabled modulu. Modul zaregistruje Quartz job a trigger s cronem v UTC. Job je jen scheduler adapter: resolve `IDispatcher`, posle command a skonci. Business logika patri do command handleru, ne do `IJob`.

**Napises v CRM:** `CrmReconcileJob` jen dispatchne `ReconcileCrmCommand`. Do `CrmModule.RegisterJobs` pridas cron config, napr. `Modules:Crm:Jobs:ReconcileCron`.

**Vzor RegisterJobs:**

```csharp
public void RegisterJobs(IServiceCollectionQuartzConfigurator quartz, IConfiguration configuration)
{
    var cron = configuration["Modules:Crm:Jobs:ReconcileCron"] ?? "0 0/15 * * * ?";
    var key = new JobKey("crm-reconcile");
    quartz.AddJob<CrmReconcileJob>(key);
    quartz.AddTrigger(trigger => trigger.ForJob(key)
        .WithCronSchedule(cron, x => x.InTimeZone(TimeZoneInfo.Utc)));
}
```

**Vzor jobu:**

```csharp
[DisallowConcurrentExecution]
internal sealed class CrmReconcileJob(IDispatcher dispatcher) : IJob
{
    public Task Execute(IJobExecutionContext context) =>
        dispatcher.Send(new ReconcileCrmCommand(MaxItems: 100), context.CancellationToken);
}
```

**Co si pohlidas:** Jobs host dnes pouziva in-memory Quartz store. `[DisallowConcurrentExecution]` brani overlapu jen v jedne instanci scheduleru. Pro HA nepoustej proste dve repliky; bud joby musi byt idempotentni, nebo se prejde na clustered Quartz AdoJobStore.

**Nepouzijes:** cron na event-driven praci, business logiku v `IJob`, local time zone, ani unbounded sweep bez capu.

**EC:**

- EC331 cron in UTC → vzdy `.WithCronSchedule(cron, x => x.InTimeZone(TimeZoneInfo.Utc))`.
- EC332 job nesmi obsahovat business logiku → job dispatchne command, logika je v handleru.
- EC333 multi-instance scheduling → default Quartz store neni cluster; single Jobs replica nebo idempotentni joby/cluster store.
- EC334 retry/idempotency → Quartz zopakuje dalsi tick, command musi byt idempotentni a bezpecny pri duplicitnim behu.
- EC335 cap per run → reconcile/sweep command ma limit typu `MaxItems`, aby jeden cron nezablokoval host.

### UC68 Messaging health

**Status:** Implemented + tested — `MessagingHealthEvaluationTests` a `JobFailureMetricsTests`.

**Pouzijes:** `MessagingHealthJob`.

**Co se stane:** Jobs host pravidelne spousti `MessagingHealthJob`. Job se pta Wolverine pres `IMessageStore.Admin.FetchCountsAsync()`, ne pres SQL nad internimi tabulkami. Aktualizuje OTel gauges `platform.messaging.dead_letters`, `platform.messaging.incoming_pending`, `platform.messaging.outgoing_pending` a zaloguje warning, kdyz existuje DLQ nebo pending count prekroci `Messaging:StuckThreshold`.

**Napises v CRM:** vlastni domenu reconcile, pokud stuck run potrebuje opravu. Messaging health rekne "neco je v DLQ / outbox se zasekl", ale CRM musi umet rict "ktery import/run je v nekonzistentnim stavu a jak ho opravit".

**Vzor CRM reakce:**

```csharp
internal sealed class ReconcileCrmCommandHandler
{
    // Najdi stuck CrmRuns, over externi stav, znovu enqueue praci nebo oznac Failed.
}
```

**Co si pohlidas:** `Outgoing` je outbox backlog. `Scheduled` nejsou stuck messages; to jsou treba saga timeouts do budoucna. Dead-letter neni recovery mechanismus, jen signal pro operatora/reconcile.

**Nepouzijes:** raw SQL do `wolverine_*` tabulek, business opravu primo v `MessagingHealthJob`, ani alert bez runbooku.

**EC:**

- EC336 DLQ neni business recovery → DLQ signalizuje problem, domenovy reconcile musi vedet co opravit.
- EC337 alert bez akce nestaci → k alertu patri runbook: inspect, replay, nebo spustit domenovy reconcile.
- EC338 stuck threshold → `Messaging:StuckThreshold`, default 100.
- EC339 no raw SQL do Wolverine tables → pouzij `IMessageStore.Admin.FetchCountsAsync()`.
- EC340 metrics musi mit jasny owner → `platform.messaging.*` vlastni platform/Jops host; CRM vlastni `crm.*` stuck/import metrics.

### UC69 Realtime stream

**Status:** Implemented + tested — `RealtimeSseTests` a `RealtimeReplayTests`.

**Pouzijes:** `GET /realtime/stream`.

**Co se stane:** Browser otevře auth-gated SSE stream `/v1/realtime/stream`. Server posila eventy pro prihlaseneho usera. Pri reconnectu browser posle `Last-Event-ID`; platforma nejdriv pusti best-effort replay z Redis Streamu nebo local ring bufferu a pak live events.

**Napises v CRM:** event type mapping na invalidaci `queryRoots.crm`. Realtime event nema byt jediny zdroj dat; je to signal "refetchni query". Napriklad `crm.deal_updated` invaliduje detail dealu a list deals.

**Vzor frontendu:**

```ts
const es = new EventSource("/v1/realtime/stream", { withCredentials: true });
es.addEventListener("crm.deal_updated", (event) => {
  const payload = JSON.parse((event as MessageEvent).data);
  queryClient.invalidateQueries({ queryKey: ["crm", "deal", payload.dealId] });
  queryClient.invalidateQueries({ queryKey: ["crm", "deals"] });
});
```

**Vzor backend publish:** publikuj az po commitu. Pokud pouzivas outbox worker, publikuj v workeru po ulozeni stavu; pokud je to rychla in-request mutace, zavolej realtime az po `SaveChanges`.

```csharp
await realtime.PublishToUserAsync(userId, "crm.deal_updated", new { dealId }, ct);
```

**Co si pohlidas:** replay je UX smoothing, ne guarantee. Durable pravda je DB/API. Kdyz user byl dlouho offline a replay buffer uz event nema, frontend stejne musi refetchnout pri focusu/navratu.

**Nepouzijes:** vlastni WebSocket pro CRM, realtime event pred DB commitem, ani stav ulozeny jen v event payloadu.

**EC:**

- EC341 SSE disconnect → browser reconnectne a posle `Last-Event-ID`; server zkusi replay.
- EC342 replay je best-effort, ne durable truth → stale pouzivej API refetch jako pravdu.
- EC343 Redis fallback/local mode rozdily → Redis umi multi-instance fan-out + stream replay; local mode je jen single-instance dev/test ring buffer.
- EC344 event pred commitem nesmi odejit → publish az po commit, jinak UI refetchne phantom stav.
- EC345 frontend ma refetch fallback → refetch pri focusu, navigation a po mutacich, i kdyz realtime nic neprislo.

## GDPR, PII, Audit

### UC70 Export osobnich dat

**Status:** Implemented pattern + tested — `GdprIntegrationTests.Export_assembles_one_document_keyed_by_module_with_each_modules_section` a `ExportResilienceUnitTests`.

**Pouzijes:** `GET /gdpr/me/export`.

**Co se stane:** Endpoint vezme subject user id z tokenu (`/gdpr/me/export`, zadny route user id). `ExportUserDataHandler` zavola vsechny registrovane `IExportPersonalData` implementace a slozi jeden dokument keyed by `ModuleName`. Kdyz jeden exporter spadne, jeho sekce bude `{ "error": "export_failed" }` a ostatni moduly se exportuji dal.

**Napises v CRM:** `CrmPersonalDataExporter` a registraci v `CrmModule.RegisterServices`.

**Vzor exporteru:**

```csharp
internal sealed class CrmPersonalDataExporter(IReadDbContextFactory<CrmDbContext> readDb)
    : IExportPersonalData
{
    public string ModuleName => "Crm";

    public async Task<IReadOnlyDictionary<string, object?>> ExportAsync(Guid userId, CancellationToken ct)
    {
        await using var db = readDb.Create();
        var contacts = await db.Contacts
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.Name, x.Email, x.CreatedAt })
            .ToListAsync(ct);

        return new Dictionary<string, object?>
        {
            ["contacts"] = contacts,
        };
    }
}
```

**Registrace:**

```csharp
services.AddScoped<IExportPersonalData, CrmPersonalDataExporter>();
```

**Co si pohlidas:** export shape je API contract. Men ho opatrne. Encrypted columns cti pres EF model converter/read context, ne raw SQL, aby se PII spravne dešifrovala nebo vratila `[erased]`.

**Nepouzijes:** endpoint `/gdpr/users/{id}/export` pro self-service, cross-module export z GDPR Core, raw SQL nad encrypted columns.

**EC:**

- EC346 exporter chybi -> CRM data nejsou v exportu → kazdy PII modul musi registrovat `IExportPersonalData`.
- EC347 exporter throw nesmi shodit cely export → handler izoluje exporter exception a vlozi `error=export_failed`.
- EC348 export foreign user zakazan → subject id je vzdy `ITenantContext.UserId`, ne route/body.
- EC349 PII z encrypted columns se cte pres converter → pouzij EF/read DbContext, ne raw SQL.
- EC350 export format musi byt stabilni → sekce keyed by `ModuleName`, polozky pojmenuj stabilne (`contacts`, `deals`, `attachments`).

### UC71 Request erasure

**Status:** Implemented pattern + tested — `GdprIntegrationTests.Erasure_blanks_notification_pii_shreds_the_subject_key_and_retains_the_billing_ledger`, Identity erasure/session tests.

**Pouzijes:** `POST /gdpr/me/erase`.

**Co se stane:** Endpoint vezme subject user id z tokenu a `RequestErasureHandler` publikuje `UserErasureRequested` pres outbox. Worker `UserErasureRequestedHandler` zavola vsechny `IErasePersonalData` implementace a potom dispatchne `ShredSubjectKeyCommand`, ktery znici subject DEK. Crypto-shred je autoritativni erasure akt; residual rows se v modulech anonymizuji nebo mazou podle povinnosti.

**Napises v CRM:** `CrmPersonalDataEraser` a registraci v `CrmModule.RegisterServices`.

**Vzor eraseru:**

```csharp
internal sealed class CrmPersonalDataEraser(CrmDbContext db) : IErasePersonalData
{
    public string ModuleName => "Crm";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        var contacts = await db.Contacts
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);

        foreach (var contact in contacts)
        {
            contact.Name = "";
            contact.Email = "";
        }

        await db.SaveChangesAsync(ct);
    }
}
```

**Registrace:**

```csharp
services.AddScoped<IErasePersonalData, CrmPersonalDataEraser>();
```

**Co si pohlidas:** eraser musi byt idempotentni. Kdyz ho Worker zavola dvakrat, nesmi spadnout ani obnovit PII. U append-only / ucetnich dat anonymizuj PII, nemaž historickou integritu.

**Nepouzijes:** fyzicke mazani ledger/audit rows, vlastni erasure endpoint v CRM, ani zavislost GDPR Core na CRM Core.

**EC:**

- EC351 eraser chybi -> CRM PII prezije → kazdy PII modul musi registrovat `IErasePersonalData`.
- EC352 jeden eraser failure nesmi blokovat crypto-shred vseho navzdy → handler loguje failure, crypto-shred provede, pak throwne pro retry failed eraseru.
- EC353 ledger/audit se nema fyzicky mazat, ale anonymizovat → append-only data se drzi, PII se znecitliví/crypto-shredne.
- EC354 erasure retry idempotentni → opakovany POST po shredded key je no-op; erasers musi byt idempotentni.
- EC355 po erasure login/refresh nesmi fungovat → Identity soft-delete/revoke flow brani loginu a refresh rotaci.

### UC72 Grant consent

**Status:** Implemented + tested — `GdprIntegrationTests.Consent_grant_then_withdraw_is_append_only_and_get_reflects_the_latest_state` a consent export/erasure test.

**Pouzijes:** `POST /gdpr/consents/grant`.

**Co se stane:** Endpoint vezme usera z tokenu a ulozi novy append-only `ConsentRecord` s `Granted = true`, `ConsentType`, `RecordedAt`, volitelne `PolicyVersion`. Nic se neupdatuje; historie je audit trail. Aktualni stav pro consent key je posledni zaznam podle `RecordedAt`.

**Napises v CRM:** ctes consent stav z GDPR endpointu nebo query, nevytvaris paralelni consent table. Pokud CRM potrebuje napr. marketing consent, ptej se na consent key typu `crm.marketing`.

**Vzor requestu:**

```http
POST /v1/gdpr/consents/grant
{
  "consentType": "crm.marketing",
  "policyVersion": "2026-06"
}
```

**Vzor CRM guardu:**

```csharp
var hasConsent = consents
    .Where(x => x.ConsentType == "crm.marketing")
    .OrderByDescending(x => x.RecordedAt)
    .FirstOrDefault()?.Granted == true;
```

**Co si pohlidas:** duplicate grant znamena dalsi historicky zaznam, ne chyba. Pokud produkt chce whitelist consent keys, dopln centralni registry/validator; soucasny base kontroluje jen non-empty/max length.

**Nepouzijes:** vlastni CRM consent table, user id v body, ani prepis stare consent row.

**EC:**

- EC356 duplicate grant → append-only historie; latest record je current state.
- EC357 unknown consent key → base nema whitelist; pokud je potreba, pridej registry/validator pred produktovym pouzitim.
- EC358 consent musi byt audit/export → `Gdpr.Consents` implementuje export a erasure.
- EC359 frontend stale consent state → po grant invaliduj `gdpr.consents`/feature guards.
- EC360 legal text/version → posilej `policyVersion`, aby bylo dohledatelne, s jakou verzi textu user souhlasil.

### UC73 Withdraw consent

**Status:** Implemented + tested — `GdprIntegrationTests.Consent_grant_then_withdraw_is_append_only_and_get_reflects_the_latest_state`.

**Pouzijes:** `POST /gdpr/consents/withdraw`.

**Co se stane:** GDPR prida novy append-only `ConsentRecord` s `Granted = false`. Stare grant rows zustanou jako historie. Aktualni stav je posledni zaznam pro dany `ConsentType`.

**Napises v CRM:** prestanes delat akce vyzadujici consent. Frontend invaliduje consent query a CRM background joby pred provedenim znovu prectou aktualni consent state.

**Vzor requestu:**

```http
POST /v1/gdpr/consents/withdraw
{
  "consentType": "crm.marketing",
  "policyVersion": "2026-06"
}
```

**Vzor CRM checku v jobu:**

```csharp
if (!await consentReader.HasActiveConsentAsync(userId, "crm.marketing", ct))
{
    return Unit.Value;
}
```

**Co si pohlidas:** withdrawal neni delete dat. Je to pravni/produktovy signal, ze dalsi zpracovani vyzadujici souhlas ma prestat. Data retention/erasure resi UC71.

**Nepouzijes:** mazani vsech CRM dat pri withdraw, cache consentu bez expirace, ani background job spolehajici na stav nacteny pred hodinou.

**EC:**

- EC361 withdraw bez grant → validni append-only row `Granted=false`; current state je false.
- EC362 stale frontend → po withdraw invaliduj consent center i feature guards.
- EC363 background job musi znovu cist consent → pred kazdou consent-gated akcí znovu over current state.
- EC364 audit/export → consent historie je exportovana v `Gdpr.Consents`.
- EC365 consent withdrawal neni delete vsech dat → pouze zastavi dalsi zpracovani vyzadujici consent; erasure je samostatny flow.

### UC74 Get consents

**Status:** Implemented + tested — `Get_consents_has_empty_state_is_owner_scoped_and_returns_policy_version` a consent round-trip tests.

**Pouzijes:** `GET /gdpr/me/consents`.

**Co se stane:** Endpoint vrati append-only consent historii prihlaseneho usera, newest first, capped na 500 poslednich zaznamu. Response obsahuje `id`, `consentType`, `granted`, `recordedAt`, `policyVersion`. Prazdna historie je `[]`.

**Napises v CRM:** nic do backendu, pokud jen zobrazujes consent center. CRM feature guardy cti aktualni stav tak, ze vezmes newest row pro dany `consentType`.

**Vzor frontendu:**

```ts
const history = await api.get("/v1/gdpr/me/consents");
const currentMarketing = history.data
  .filter((x) => x.consentType === "crm.marketing")
  .sort((a, b) => Date.parse(b.recordedAt) - Date.parse(a.recordedAt))[0];
```

**Co si pohlidas:** legal text a lokalizace nejsou v response; response nese `policyVersion`. UI si podle consent key + locale + policyVersion nacte spravny text z vlastniho content/config layeru.

**Nepouzijes:** `GET /gdpr/users/{id}/consents`, frontend-only owner filter, ani unsupported consent key jako duvod pro cteni cizi historie.

**EC:**

- EC366 empty state → vraci `[]`, ne 404.
- EC367 locale/legal text → legal text je content/config; consent record drzi `policyVersion`.
- EC368 stale query → po grant/withdraw invaliduj `gdpr.consents`.
- EC369 owner scope → subject je token user, test overuje cizi consent se nevrati.
- EC370 unsupported consent key → base nema whitelist; UI muze neznamy key ignorovat nebo zobrazit fallback.

### UC75 PII v CRM datech

**Status:** Blueprint nad hotovym base mechanismem — CRM modul ho zkopiruje z Identity/Notifications a overi vlastnimi testy.

**Pouzijes:** `[PersonalData]`, `[Encrypted]`, `IDataSubject`, `IBlindIndexHasher`, GDPR crypto-shredder.

**Co se stane:** CRM kontakt muze mit email, jmeno, telefon nebo poznamku s osobnimi udaji, ale v databazi nelezi jako cisty text. Live sloupec se ulozi jako encrypted envelope `penc:v2...`, audit PII hodnoty jsou chranene pres subject key a po GDPR erasure se daji znecitelnit crypto-shreddem. Aplikace pri normalnim cteni dostane plaintext; po shred vrati `[erased]`.

**Mentalni model:** `[Encrypted]` chrani aktualni hodnotu v tabulce, `[PersonalData]` rika auditu/GDPR "tohle je osobni udaj" a `IDataSubject.SubjectId` rika, komu ten udaj patri. U CRM kontaktu typicky nechces subjectem delat samotny kontakt, ale ownera z tokenu (`UserId`), aby erasure uzivatele znicila jeho CRM PII.

**Napises v CRM:** entity oznacis atributy, pridas hash sloupec pro lookup a v handleru nikdy nehledas podle encrypted hodnoty.

```csharp
internal sealed class CrmContact : AuditableEntity, ITenantScoped, IUserOwned, IDataSubject
{
    public Guid UserId { get; set; }

    [PersonalData]
    [Encrypted]
    public string Email { get; set; } = string.Empty;

    public string EmailHash { get; set; } = string.Empty;

    [PersonalData]
    [Encrypted]
    public string DisplayName { get; set; } = string.Empty;

    Guid IDataSubject.SubjectId => UserId;
}
```

**Lookup podle emailu:** encrypted sloupec nejde pouzit na `WHERE Email == ...`, protoze v DB je ciphertext. Napises normalizaci a ulozis blind index.

```csharp
var normalizedEmail = request.Email.Trim().ToUpperInvariant();
var emailHash = blindIndexHasher.Hash(normalizedEmail);

var contact = await db.Contacts
    .Where(x => x.UserId == tenant.UserId)
    .FirstOrDefaultAsync(x => x.EmailHash == emailHash, ct);
```

**Create/update:** pred ulozenim nastav plaintext do `Email`; interceptor ho pri `SaveChanges` zasifruje. Zaroven nastav `EmailHash`, protoze ten se pouziva na duplicate check a lookup. Hash neni plaintext email, ale porad se k nemu chovej jako k citlivemu technickemu identifikatoru.

```csharp
contact.Email = normalizedEmail;
contact.EmailHash = blindIndexHasher.Hash(normalizedEmail);
contact.DisplayName = request.DisplayName.Trim();

await db.SaveChangesAsync(ct);
```

**Cross-module event:** kdyz CRM publikuje `CrmContactCreatedIntegrationEvent`, neposilej v nem email/body/poznamku jen proto, ze se to hodi consumerovi. Posli `ContactId`, `OwnerUserId`, pripadne ne-PII business stav. Consumer si PII dotahne pres povoleny query/contract, pokud k tomu ma opravneni.

```csharp
public sealed record CrmContactCreatedIntegrationEvent(
    Guid ContactId,
    Guid OwnerUserId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
```

**Testy k CRM:** pridej architecture test pro atributy, integracni test "DB obsahuje `penc:v2`, API vraci plaintext", duplicate email test pres `EmailHash` a GDPR erasure test, ze po shred API nevraci puvodni hodnotu.

**Nepouzijes:** plaintext search nad encrypted sloupcem, vlastni AES helper, event payload s nepotrebnou PII, route/body `userId` jako subject, ani `[PersonalData]` bez `IDataSubject`.

**EC:**

- EC371 encrypted column nejde hledat plaintextem → pro email/telefon vytvor normalizovanou hodnotu + blind index (`EmailHash`, `PhoneHash`) a query pis proti hashi.
- EC372 blind index key missing fail-fast → mimo Development musi byt `Gdpr:Encryption:BlindIndexKey` realny secret; jinak appka spadne pri startu, coz je spravne.
- EC373 po shred se hodnota cte `[erased]` → UI to bere jako terminalni privacy stav, ne jako chybu k retry.
- EC374 atribut `[PersonalData]` bez `IDataSubject` je arch bug → entity musi implementovat `IDataSubject`, jinak architecture test selze.
- EC375 neukladat PII do event payloadu zbytecne → durable envelope neni crypto-shreddable stejne jako DB radek; eventy maji nest ID/stav, ne email texty a dlouhe poznamky.

### UC76 Audit zmen

**Status:** Base pattern hotovy a otestovany v Identity/Billing/Notifications; CRM modul jen pouzije tracked entities a pripadne prida vlastni audit read slice.

**Pouzijes:** `AuditInterceptor`, `AuditableEntity`, tracked EF entity, `PlatformPermissions.AuditRead`, per-module `{module}_audit_entries`.

**Co se stane:** Jakmile handler meni tracked entitu a zavola `SaveChanges`, platforma sama zapise audit radek do audit tabulky daneho modulu, napr. `crm_audit_entries`. Na create/delete ulozi vsechny auditable sloupce; na update ulozi jen zmenene sloupce. Audit row obsahuje `EntityType`, `EntityId`, `Action`, `ChangedColumns`, `NewValues`, `UserId`, `TenantId`, `IpAddress`, `Timestamp`.

**Mentalni model:** audit neni samostatny CRM service. Audit je side effect EF `SaveChanges`. Kdyz entitu nactes tracked, zmenis properties a ulozis, audit vznikne. Kdyz pouzijes `ExecuteUpdate`, `ExecuteDelete` nebo raw SQL, audit interceptor se nespusti.

**Napises v CRM:** entity dej `AuditableEntity`, zmeny delaj v command handleru pres tracked load a `SaveChanges`.

```csharp
internal sealed class CrmContact : AuditableEntity, ITenantScoped, IUserOwned
{
    public Guid UserId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Stage { get; set; } = "lead";
}
```

```csharp
var contact = await db.Contacts
    .FirstOrDefaultAsync(x => x.Id == command.ContactId
                              && x.UserId == tenant.UserId, ct)
    ?? throw new NotFoundException("crm.contact_not_found", "Contact not found.");

contact.Stage = command.Stage;
contact.CompanyName = command.CompanyName.Trim();

await db.SaveChangesAsync(ct);
```

**Jak to bude v auditu:** update ulozi napr. `ChangedColumns = ["Stage","CompanyName"]` a `NewValues` jen pro tyto zmenene hodnoty. `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy` doplni interceptor automaticky u `AuditableEntity`.

**Audit read endpoint pro CRM:** kdyz chces ukazat historii kontaktu, udelej samostatnou query slice, napr. `GetContactAuditTrail`. Query cte `db.AuditEntries` no-tracking, filtruje `EntityType == "CrmContact"` a `EntityId == contactId.ToString()`. Endpoint musi mit `.RequirePermission(PlatformPermissions.AuditRead)`.

```csharp
var entityId = query.ContactId.ToString();
var rows = await db.AuditEntries
    .Where(a => a.EntityType == "CrmContact" && a.EntityId == entityId)
    .OrderByDescending(a => a.Timestamp)
    .ToListAsync(ct);
```

**PII v auditu:** pokud je audited property `[PersonalData]`, audit JSON nedostane plaintext. Dostane protected envelope pres `IPersonalDataProtector`; admin forensic query ho umi odhalit, dokud existuje subject key. Po GDPR shred se ve vystupu ukaze `[erased]`.

**User context vs system context:** HTTP request zapise do audit row realneho usera/tenant/IP z tokenu. Worker nebo Jobs bezi casto jako system context, takze `UserId` muze byt prazdny. Kdyz CRM potrebuje pozdeji vysvetlit "kdo spustil import", uloz iniciatora do vlastni operation/import entity a audituj tu entitu tracked save; nespolihaj, ze worker audit row bude mit stejneho usera jako puvodni klik v UI.

**Kdy pouzit `ExecuteUpdate`:** jen kdyz je zmena zamerne auditovana jinak nebo audit nepotrebujes. Typicky billing debit guard nebo GDPR scrub. U CRM statusu, poznamek, prirazeni a vlastniku pouzij tracked entity, protoze tyhle zmeny budou chtit lidi dohledat.

**Testy k CRM:** pridej test create/update audit row, test update uklada jen changed columns, test audit endpoint vyzaduje `audit.read`, test user/tenant scope a PII audit test, pokud CRM audit obsahuje `[PersonalData]`.

**Nepouzijes:** vlastni `CrmAuditLog` service, manualni insert do audit table, audit v endpointu, raw SQL, ani `ExecuteUpdate` pro business zmeny, ktere maji byt forenzne dohledatelne.

**EC:**

- EC376 `ExecuteUpdate` bypassuje audit → pouzij ho jen jako vedome vyjimky; jinak tracked load + property set + `SaveChanges`.
- EC377 raw SQL bypassuje conventions → v ModularPlatform raw SQL nepis; rozbije audit, RLS/conventions a udrzovatelnost.
- EC378 PII v auditu encryptovat → `[PersonalData]` hodnota se v `NewValues` uklada jako protected envelope, ne plaintext.
- EC379 audit read permission → audit endpointy musi byt za `PlatformPermissions.AuditRead`, protoze umi ukazat citlive forenzni informace.
- EC380 system context vs user context → worker/job audit muze byt systemovy; puvodni iniciator dlouhe akce patri do operation/import entity, ne do domenky z audit `UserId`.

### UC77 Crypto-shred

**Status:** Base mechanismus hotovy a otestovany; CRM modul musi dodat vlastni `IErasePersonalData`, pokud drzi PII.

**Pouzijes:** `POST /v1/gdpr/me/erase`, `UserErasureRequested`, `IErasePersonalData`, `ShredSubjectKeyCommand`, `subject_keys`.

**Co se stane:** Erasure neni "smaz vsechny radky". Platforma publikuje durable `UserErasureRequested`. Worker zavola vsechny `IErasePersonalData` implementace, aby si kazdy modul anonymizoval svoje zbytky. Nakonec GDPR modul shreduje subject key: v `subject_keys` nastavi `WrappedDek = null` a `DeletedAt`. Od te chvile nejde precist zadny encrypted/audit envelope pod timto subjectem.

**Mentalni model:** per-module eraser uklidi ciste texty nebo business radky, ktere musi zustat. Crypto-shred je autoritativni privacy akt pro encrypted PII a audit PII. Backupy nebo append-only audit nemusi fyzicky prepisovat stare ciphertexty, protoze bez DEK jsou nepouzitelne.

**Napises v CRM:** pokud CRM drzi kontakty, poznamky, import snapshots nebo AI analyzy s PII, zaregistruj `IErasePersonalData` v `CrmModule.RegisterServices`.

```csharp
services.AddScoped<IErasePersonalData, CrmPersonalDataEraser>();
```

```csharp
internal sealed class CrmPersonalDataEraser(CrmDbContext db) : IErasePersonalData
{
    public string ModuleName => "Crm";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        await db.Contacts
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Email, string.Empty)
                .SetProperty(x => x.EmailHash, string.Empty)
                .SetProperty(x => x.DisplayName, "[erased]")
                .SetProperty(x => x.Notes, string.Empty), ct);
    }
}
```

**Kdy pouzit `ExecuteUpdate` tady:** u erasure je set-based scrub spravne, i kdyz bypassuje audit/encryption interceptor. Nepises tam PII, ale tombstone konstanty. Operace musi byt idempotentni: opakovany erasure request nesmi vratit chybu a nesmi znovu vytvaret PII.

**Co nedelas v CRM:** CRM samo neshreduje `subject_keys`; to dela GDPR modul po fan-outu. CRM take nema volat ostatni moduly. Jen uklidi svoji cast a necha event handler dobehnout.

**Flow v base:** request handler publikuje `UserErasureRequested` pres outbox. Worker zavola erasers. I kdyz jeden eraser spadne, crypto-shred stale probehne a message se retryne, aby se failed modul pozdeji docistil.

**Frontend UX:** po `POST /gdpr/me/erase` ber user account jako terminalni stav. Odhlas ho, zrus local cache, nepokousej se znovu nacitat CRM detail. Pokud UI nekde uvidi `[erased]`, zobrazi privacy-safe placeholder a nepusti editaci erased profilu.

**Testy k CRM:** vytvor kontakt s PII, zavolej erasure, over ze CRM plaintext/lookup sloupce jsou anonymizovane, encrypted/audit read vraci `[erased]`, opakovany erasure je no-op a cizi user data zustanou nedotcena.

**Nepouzijes:** fyzicky delete ledger/audit rows, custom crypto shred v CRM, mazani `subject_keys` radku, novy DEK po erasure, ani cross-module direct calls.

**EC:**

- EC381 tombstone se nesmi smazat → `subject_keys` radek s `WrappedDek = null` zustava permanentne, aby pozdejsi write nemohl vytvorit novy citelny DEK.
- EC382 post-erasure write PII nesmi remintnout readable key → protector po shredded key vraci redacted marker, ne novou sifrovatelnou hodnotu.
- EC383 `[erased]` nesmi rozbit validators/UI → UI musi umet read-only erased placeholder; update flow nema vynucovat validni email na erased hodnote.
- EC384 admin forensic read po shred vraci erased → audit endpoint nesmi spadnout ani vratit stary plaintext; protected envelope se mapuje na `[erased]`.
- EC385 cache s plaintext PII musi expirovat/refetchnout → po erasure invaliduj CRM/profile/notifications cache a vycisti local storage, jinak UI ukaze stare PII z klienta.

### UC78 Retention sweep

**Status:** Base GDPR job hotovy a otestovany; pro CRM je to vzor, ne misto pro cross-module mazani.

**Pouzijes:** `GdprRetentionSweepJob`, Quartz cron, vlastni `CrmRetentionSweepCommand`, vlastni CRM job jen pro CRM-owned data.

**Co se stane:** GDPR retention sweep bezi jako Quartz job a dispatchuje command, ale dnes nic nepuruje. Duvod je zamer: shredded `subject_keys` tombstones se musi drzet permanentne, protoze brani tomu, aby se po erasure vytvoril novy readable DEK pro stejny subject.

**Mentalni model:** erasure a retention nejsou to same. Erasure odstrani/anonymizuje PII konkretniho subjektu. Retention sweep je casovy uklid dat, ktera uz podle policy nepotrebujes. CRM si muze mazat jen vlastni purgeable data, napr. stare import temporary rows, failed pull logs nebo expirovane AI drafty. Nesmí sahat na GDPR `subject_keys`, audit tombstones ani cizi moduly.

**Napises v CRM:** pokud CRM ma purgeable data, udelej command a ten volej z jobu. Job je tenky; business logika je v handleru.

```csharp
internal sealed class CrmRetentionSweepJob(IDispatcher dispatcher) : IJob
{
    public Task Execute(IJobExecutionContext context) =>
        dispatcher.Send(new CrmRetentionSweepCommand(), context.CancellationToken);
}
```

```csharp
internal sealed class CrmRetentionSweepHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<CrmRetentionSweepCommand, CrmRetentionSweepResponse>
{
    public async Task<CrmRetentionSweepResponse> Handle(CrmRetentionSweepCommand command, CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddDays(-30);

        var deleted = await db.ImportDrafts
            .Where(x => x.Status == ImportDraftStatus.Failed && x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        return new CrmRetentionSweepResponse(deleted);
    }
}
```

**Registrace jobu:** v `CrmModule.RegisterJobs` pridej trigger s cronem z configu, napr. `Modules:Crm:Jobs:RetentionSweepCron`. Cron interpretuj jako UTC a dej cap per run, pokud ma tabulka rust hodne.

**Co purgeovat v CRM:** do doc/spec si u kazde tabulky rekni, jestli je to legal/audit/business record nebo jen temporary cache. Temporary import chunks ano. CRM contacts vetsinou ne, dokud existuje owner nebo legal/business retention. Audit rows ne.

**Testy k CRM:** testuj, ze sweep smaze jen stare purgeable CRM rows, nesmaze nove rows, je idempotentni, nesaha na subject key tombstones a Jobs host nabootuje s registovanou job dependency graph.

**Nepouzijes:** generic cross-module reaper, raw SQL delete, mazani `subject_keys`, mazani audit entries bez jasne policy, ani business logiku primo v `IJob`.

**EC:**

- EC386 tombstones se nemazou → shredded `subject_keys` jsou permanentni DEK re-mint guard; CRM sweep se jich nikdy nedotyka.
- EC387 cron UTC → job planuj v UTC a pojmenuj config klic, napr. `Modules:Crm:Jobs:RetentionSweepCron`.
- EC388 purge jen module-owned data → CRM maze jen CRM tabulky; cross-module cleanup je poruseni boundary.
- EC389 legal retention vs user erase → pred delete rozlis temporary data od zaznamu, ktere musi zustat pro audit/legal/business historii.
- EC390 idempotentni sweep → opakovany run po prvnim cleanupu vrati 0 nebo stejny bezpecny vysledek, ne chybu.

## Marketing

### UC79 Spustit data pull

**Status:** Implementovany kanonicky Marketing 202 pattern; CRM import/sync ho ma kopirovat.

**Pouzijes:** `POST /marketing/pulls`, `IDbContextOutbox`, durable `RunDataPull`, worker handler, gateway porty (`IGa4Gateway`, `IGscGateway`).

**Co se stane:** HTTP request jen prijme praci, ulozi `DataPull` ve stavu `Pending`, publikuje durable work message pres outbox a vrati `202 Accepted + Location`. Externi GA/GSC API se nevola v requestu. Worker pozdeji zmeni stav `Pending -> Running -> Completed/Failed`, ulozi snapshots a po commitu posle realtime invalidaci.

**Mentalni model:** tohle je vzor pro jakykoliv CRM import z HubSpotu, CSV enrich, lead sync nebo externi scoring. User klikne "spustit", backend zalozi jednotku prace a UI polluje status. Vse pomale, retryovatelne a nespolehlive bezi ve Workeru.

**Endpoint:** owner ber z tokenu, defaultni datumy/casove okno pocitej pres `IClock`, vrat `202` a `Location` na status endpoint.

```csharp
app.MapPost("/crm/imports", async (
        StartCrmImportRequest request,
        ITenantContext tenant,
        IDispatcher dispatcher,
        LinkGenerator links,
        HttpContext http,
        CancellationToken ct) =>
    {
        var userId = tenant.UserId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        var result = await dispatcher.Send(
            new StartCrmImportCommand(userId, request.Source, request.FileId), ct);

        var location = links.GetPathByName(http, "GetCrmImportStatus", new { importId = result.ImportId })
            ?? $"/crm/imports/{result.ImportId}";

        return Results.Accepted(location, ApiResponse<StartCrmImportResponse>.Ok(result));
    })
    .RequireAuthorization()
    .RequireModule("crm");
```

**Accept handler:** uloz `Pending` radek a durable message v jedne outbox transakci. Kdyz ulozeni projde, message existuje. Kdyz ulozeni spadne, message nevznikne.

```csharp
var import = new CrmImport
{
    UserId = command.UserId,
    Source = command.Source,
    Status = ImportStatus.Pending,
    ParamsJson = JsonSerializer.Serialize(new { command.FileId }),
};

outbox.DbContext.Imports.Add(import);
await outbox.PublishAsync(new RunCrmImport(import.Id));
await outbox.SaveChangesAndFlushMessagesAsync();

return new StartCrmImportResponse(import.Id);
```

**Worker shell:** public Wolverine handler ma byt tenky. Jen prelozi durable message na interni command, aby business logika zustala ve vertical slice.

```csharp
public sealed class RunCrmImportHandler
{
    public Task Handle(RunCrmImport message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new ExecuteCrmImportCommand(message.ImportId), ct);
}
```

**Execute handler:** nacti import, ignoruj terminalni stavy, nastav `Running`, volej gateway port, uloz vysledek, nastav `Completed`. Pri exception nastav terminalni `Failed` + error code, at UI nepolluje donekonecna.

**Duplicate/idempotency:** pokud user rychle klikne dvakrat a import nemuze bezet dvakrat, pridej idempotency key/unique index podle `UserId + Source + FileId + Period`. Pokud duplicitni import je povoleny, nech to explicitne ve specu.

**Testy k CRM:** test `202 + Location`, worker dokonci import, status prejde do terminal state, owner scoping vraci cizimu userovi 404, unsupported source je validation error, gateway exception skonci jako `Failed`.

**Nepouzijes:** externi API call v endpointu, fire-and-forget `Task.Run`, vlastni background thread, handler direct call, ani request body `userId`.

**EC:**

- EC391 external API down → execute handler chyti exception, zaloguje ji a nastavi `Failed` s obecným error codem; real detail zustava v logu.
- EC392 credentials missing → gateway port failne jako config chyba; status nesmi zustat `Running`.
- EC393 duplicate pull → rozhodni idempotency key; bud vrat existujici pending import, nebo povol novy import jako samostatnou praci.
- EC394 rate limit → gateway/worker retry patri do Workeru; HTTP accept porad vraci rychle `202`.
- EC395 worker retry and status → redelivery nesmi znovu spustit terminalni `Completed/Failed` import; terminalni stav je finalni.

### UC80 Get pull status

**Status:** Implementovano v Marketing; CRM import/status endpoint ma kopirovat stejny read pattern.

**Pouzijes:** `GET /marketing/pulls/{dataPullId}`, `IReadDbContextFactory`, owner `UserId` z tokenu, `PullStatusResponse`.

**Co se stane:** Frontend po `202 Accepted` vola status endpoint, dokud stav neni terminalni. Response nese `Id`, `Source`, `Status`, `ErrorCode`, `CompletedAt`. Backend status jen cte; nespousti praci znovu.

**Mentalni model:** `GET status` je pravda pro UI. `Pending/Running` znamena "cekam, polluj dal". `Completed` znamena "hotovo, invaliduj list/snapshots/detail". `Failed` znamena "terminalni chyba, ukaz bezpecnou hlasku podle `ErrorCode`". Cizi nebo neexistujici id je vzdy 404, ne 403, aby se nedaly enumerovat ids.

**Napises v CRM:** query record musi mit `ImportId` i `UserId`. `UserId` bere endpoint z `ITenantContext`, nikdy z route/body.

```csharp
public sealed record GetCrmImportStatusQuery(Guid ImportId, Guid UserId)
    : IQuery<CrmImportStatusResponse>;

public sealed record CrmImportStatusResponse(
    Guid Id,
    string Source,
    string Status,
    string? ErrorCode,
    DateTimeOffset? CompletedAt);
```

```csharp
var import = await db.Imports
    .Where(x => x.Id == query.ImportId && x.UserId == query.UserId)
    .FirstOrDefaultAsync(ct)
    ?? throw new NotFoundException("crm.import_not_found", "Import not found.");

return new CrmImportStatusResponse(
    import.Id,
    import.Source,
    import.Status.ToString(),
    import.ErrorCode,
    import.CompletedAt);
```

**Endpoint:** zustava tenky, jen vezme usera z tokenu a zavola query.

```csharp
app.MapGet("/crm/imports/{importId:guid}", async (
        Guid importId,
        ITenantContext tenant,
        IDispatcher dispatcher,
        CancellationToken ct) =>
    {
        var userId = tenant.UserId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        var result = await dispatcher.Query(new GetCrmImportStatusQuery(importId, userId), ct);
        return Results.Ok(ApiResponse<CrmImportStatusResponse>.Ok(result));
    })
    .RequireAuthorization()
    .RequireModule("crm")
    .WithName("GetCrmImportStatus");
```

**Frontend polling:** po startu polluj kratce rychleji, pak backoff. Polling ukonci pri `Completed` nebo `Failed`. Na `Completed` invaliduj/importni data; na `Failed` zobraz lokalizovanou chybu podle `ErrorCode` a nech tlacitko "Retry" spustit novy import, ne znovu volat status.

**Stuck processing:** status endpoint nema sam opravovat stuck joby. Pokud import visi dlouho v `Running`, UI muze ukazat "trva dele nez obvykle" a backend ma mit reconciliation/job nebo retry politiku ve Workeru.

**Testy k CRM:** owner dostane `200`, cizi user dostane `404`, neexistujici id `404`, failed import vraci `ErrorCode`, polling test pocka az worker prejde do terminal state.

**Nepouzijes:** status endpoint jako trigger worku, klientsky `userId`, 403 pro foreign id, nekonecny polling bez backoffu, ani raw exception text v response.

**EC:**

- EC396 foreign pull -> 404 → filtruj `Id && UserId`, aby cizi id vypadalo stejne jako neexistujici.
- EC397 not found → `NotFoundException("crm.import_not_found", ...)`, ne `null` response.
- EC398 stuck processing → UI ma timeout/backoff; oprava stuck stavu patri do worker/reconciliation logiky, ne do GET endpointu.
- EC399 failed with reason → response nese stabilni `ErrorCode`, ne stacktrace ani provider message.
- EC400 frontend polling backoff → polluj jen po startu/status obrazovce, zastav na terminalnim stavu a invaliduj cache.

### UC81 List pulls

**Status:** Implementovano v Marketing; CRM ma kopirovat paged owner-scoped list pro importy/runy.

**Pouzijes:** `GET /marketing/pulls`, `PageRequest`, `PagedResponse<T>`, `IReadDbContextFactory`, owner `UserId` z tokenu.

**Co se stane:** User vidi historii svych pullu/importu od nejnovejsiho. Response je paged list, kazda polozka ma stejny shape jako status detail: id, source, status, errorCode, completedAt. Backend filtruje podle ownera a RLS je dalsi pojistka.

**Mentalni model:** list je pro prehled a retry UX. Neprovadi side effects, neopravuje failed/stuck prace a necte cizi historii. Frontend z nej kresli tabulku "posledni importy" a detail/status si muze otevrit pres `GET /crm/imports/{id}`.

**Napises v CRM:** query dostane `UserId` a `PageRequest`; endpoint vezme `page/pageSize` z query stringu.

```csharp
public sealed record ListCrmImportsQuery(Guid UserId, PageRequest Page)
    : IQuery<PagedResponse<CrmImportStatusResponse>>;
```

```csharp
return await db.Imports
    .Where(x => x.UserId == query.UserId)
    .OrderByDescending(x => x.CreatedAt)
    .Select(x => new CrmImportStatusResponse(
        x.Id,
        x.Source,
        x.Status.ToString(),
        x.ErrorCode,
        x.CompletedAt))
    .ToPagedResponseAsync(query.Page, ct);
```

**Endpoint:** nepousti `userId` z query stringu. Pagination parametry jsou jedine klientem ovlivnene vstupy.

```csharp
app.MapGet("/crm/imports", async (
        int? page,
        int? pageSize,
        ITenantContext tenant,
        IDispatcher dispatcher,
        CancellationToken ct) =>
    {
        var userId = tenant.UserId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        var result = await dispatcher.Query(
            new ListCrmImportsQuery(userId, new PageRequest(page, pageSize)), ct);

        return Results.Ok(ApiResponse<PagedResponse<CrmImportStatusResponse>>.Ok(result));
    });
```

**Frontend:** empty state je normalni `items: []`, ne chyba. Failed items v historii nech, protoze user potrebuje videt proc import neprosel a pripadne spustit novy. Po startu/complete importu invaliduj list query.

**Testy k CRM:** prazdny list vraci 200 + empty page, newest-first sort, owner A nevidi owner B importy, failed import zustava v listu s errorCode, pagination drzi pageSize.

**Nepouzijes:** neomezeny list bez paging, client-side owner filter, sort podle nahodneho DB orderu, mazani failed polozek jen aby UI bylo ciste.

**EC:**

- EC401 paging → pouzij `PageRequest`/`PagedResponse`, nikdy nevracej celou historii bez limitu.
- EC402 sort order → default `CreatedAt desc`, aby nove importy byly nahore a UI neskakalo nahodne.
- EC403 owner scope → `WHERE UserId == token user`; cizi rows se nedostanou ani do countu.
- EC404 old failed items → failed importy nech v historii s `ErrorCode`, retry je nova akce.
- EC405 empty state → 200 s prazdnou page; frontend zobrazi prazdnou historii, ne error.

### UC82 List snapshots

**Status:** Implementovano v Marketing; CRM ma kopirovat pattern pro ulozene projekce/snapshots z importu.

**Pouzijes:** `GET /marketing/snapshots`, `MetricSnapshot`, `IUserOwned`, `PageRequest`, optional filter, read model/projekce.

**Co se stane:** Worker po pullu prevede raw provider vysledek na normalizovane snapshots. UI pak necita raw JSON z externiho API, ale rychly read model: source, metricName, dimension, value, detailJson, recordedAt. Endpoint vraci paged list, owner-scoped a volitelne filtrovany podle source.

**Mentalni model:** snapshot je stav k precteni, ne prikaz a ne log chyby. U CRM to muze byt napr. "importovane firmy", "enrichment score", "deduplikacni kandidat", "lead score v case" nebo "stav kontaktu z externiho systemu". Kdyz data budes ukazovat casto, preloz je ve workeru do vlastni tabulky misto toho, aby frontend pokazde parsoval raw import payload.

**Entity pattern:** high-volume read model muze dedit `Entity` misto `AuditableEntity`, pokud nepotrebujes audit kazdeho bodu. Musi byt `IUserOwned` nebo `ITenantScoped` podle toho, kdo snapshot vlastni.

```csharp
internal sealed class CrmSnapshot : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid ImportId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SnapshotType { get; set; } = string.Empty;
    public string? Dimension { get; set; }
    public string DetailJson { get; set; } = "{}";
    public DateTimeOffset RecordedAt { get; set; }
    public int SchemaVersion { get; set; } = 1;
}
```

**List query:** filtruj ownera, volitelny source/type, sortuj od nejnovejsiho a vracej page.

```csharp
var snapshots = db.CrmSnapshots.Where(x => x.UserId == query.UserId);

if (!string.IsNullOrWhiteSpace(query.Source))
{
    snapshots = snapshots.Where(x => x.Source == query.Source);
}

return await snapshots
    .OrderByDescending(x => x.RecordedAt)
    .Select(x => new CrmSnapshotListItem(
        x.Id, x.Source, x.SnapshotType, x.Dimension, x.DetailJson, x.RecordedAt, x.SchemaVersion))
    .ToPagedResponseAsync(query.Page, ct);
```

**Unknown filter:** Marketing u neznameho `source` vraci empty page, ne validation error. Pro CRM je to vhodne, pokud source filter pochazi z UI tab/filteru a nechces rozbijet list.

**Stale snapshot:** snapshot neni automaticky live data. Response by mela mit `RecordedAt` nebo `ImportedAt`, aby UI mohlo ukazat stari dat a nabidnout "spustit novy import".

**Schema version:** pokud `DetailJson` muze menit shape, pridej `SchemaVersion`. Frontend i AI analyzy pak vi, jak interpretovat stare snapshots.

**Testy k CRM:** no snapshots vraci empty page, source/type filter funguje, owner scoping drzi, paging funguje, sort je `RecordedAt desc`, stary schemaVersion se neplete s novym.

**Nepouzijes:** raw provider payload jako UI contract, list bez paging, frontend-only filter nad cizimi daty, ani snapshot bez casu/schema, pokud se ma dlouho uchovavat.

**EC:**

- EC406 no snapshots → 200 + empty page; UI ukaze "zatim zadna data", ne error.
- EC407 stale snapshot → response nese `RecordedAt`/`ImportedAt`; UI pozna stara data a muze nabidnout refresh/import.
- EC408 paging → snapshots mohou rust rychle, vzdy pouzij `PageRequest`/`PagedResponse`.
- EC409 owner/tenant scope → filtruj `UserId`/`TenantId` v query a oznac entitu `IUserOwned`/`ITenantScoped`.
- EC410 schema version → u JSON detailu pridej `SchemaVersion`, aby stare ulozene projekce nerozbily novy frontend/AI reader.

### UC83 List analyses

**Status:** Implementovano v Marketing; CRM ma kopirovat pattern pro ulozene AI insighty.

**Pouzijes:** `GET /marketing/analyses`, `MarketingAnalysis`, `IMarketingAiGateway`, `PageRequest`, owner `UserId` z tokenu.

**Co se stane:** Worker po completed pullu nacte snapshots, zavola AI gateway, ulozi `MarketingAnalysis` a az pak UI cte list analyz. List endpoint uz AI nevola; jen vraci ulozene zaznamy od nejnovejsiho.

**Mentalni model:** AI vystup je domenovy artefakt. Kdyz CRM vygeneruje "deal risk analysis", "lead quality insight" nebo "next best action", uloz ho do DB s vazbou na vstupni import/kontakt/deal. Jinak ho neumi GDPR export, audit, detail endpoint ani historie UI.

**Entity pattern:** dej ownera, vazbu na zdroj dat, kratky summary pro list a strukturovany JSON pro detail.

```csharp
internal sealed class CrmAnalysis : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid? ImportId { get; set; }
    public Guid? DealId { get; set; }
    public string AnalysisType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? InsightsJson { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }
}
```

**List query:** vracej jen metadata pro tabulku/list. Velky `InsightsJson` nech pro detail endpoint UC84.

```csharp
return await db.CrmAnalyses
    .Where(x => x.UserId == query.UserId)
    .OrderByDescending(x => x.AnalyzedAt)
    .Select(x => new CrmAnalysisListItem(
        x.Id,
        x.AnalysisType,
        x.Summary,
        x.AnalyzedAt))
    .ToPagedResponseAsync(query.Page, ct);
```

**Worker write:** AI gateway volej ve worker commandu, ne v list endpointu. Pokud nejsou snapshots/vstupy, analysis nevytvarej nebo vytvor explicitni failed/empty stav podle produktu.

**Failed analysis:** Marketing dnes failed AI analysis neuklada jako analysis row; chyba by se projevila worker retry/DLQ. U CRM si vyber: bud failed zustane stav importu/runu, nebo pridej `Status`/`ErrorCode` na `CrmAnalysisRun`. Nevracej fake insight.

**Erased user data:** pokud erasure smazala vstupy, analysis list ma bud zmizet pres `IErasePersonalData`, nebo zustat jen anonymizovana. UI nesmi ukazovat cached insight s PII po erasure.

**Testy k CRM:** empty list vraci 200, list je paged/newest-first, owner scope drzi, summary neobsahuje velky detail JSON, po GDPR erasure se PII insighty smazou/anonymizuji.

**Nepouzijes:** AI call v GET endpointu, transient-only AI odpoved bez ulozeni, raw prompt/output s PII v listu, ani client-side owner filter.

**EC:**

- EC411 empty list → 200 + empty page; user jeste nema zadne insighty.
- EC412 failed analysis → failed stav eviduj u runu/importu nebo analysis statusu; nevymyslej fake summary.
- EC413 stale data source → list item ma `AnalyzedAt` a idealne vazbu na `ImportId`/snapshot version, aby UI poznalo stary insight.
- EC414 paging → AI historie muze rust; pouzij `PageRequest` a nevracej velke JSON detaily v listu.
- EC415 erased user data → GDPR eraser musi AI vystupy s PII smazat/anonymizovat a frontend vycistit cache.

### UC84 Get analysis

**Status:** Implementovano v Marketing; CRM detail analysis ma kopirovat owner-scoped read pattern.

**Pouzijes:** `GET /marketing/analyses/{analysisId}`, `IReadDbContextFactory`, `NotFoundException`, owner `UserId` z tokenu.

**Co se stane:** Backend vrati jeden ulozeny AI insight vcetne detailniho `InsightsJson`, vazby na data pull/import a `AnalyzedAt`. Cizi nebo neexistujici analysis id vraci 404. GET nic negeneruje a nevola AI.

**Mentalni model:** list analysis je rychly prehled, detail analysis je plny dokument. Tady muze byt delsi JSON pro UI sekce, actions, risks, citations/reference na snapshots. Pokud detail neni hotovy, nedelej polovicni magii v GET; stav hotovosti patri do run/status modelu.

**Query pattern:** query dostane `AnalysisId` a `UserId`. UserId vzdy z tokenu.

```csharp
public sealed record GetCrmAnalysisQuery(Guid AnalysisId, Guid UserId)
    : IQuery<CrmAnalysisDetail>;
```

```csharp
var analysis = await db.CrmAnalyses
    .Where(x => x.Id == query.AnalysisId && x.UserId == query.UserId)
    .FirstOrDefaultAsync(ct)
    ?? throw new NotFoundException("crm.analysis_not_found", "Analysis not found.");

return new CrmAnalysisDetail(
    analysis.Id,
    analysis.AnalysisType,
    analysis.Summary,
    analysis.InsightsJson,
    analysis.ImportId,
    analysis.AnalyzedAt);
```

**Response shape:** pro detail vrat `InsightsJson`, ale listu ho nedavej. Do detailu pridej source/import/schema metadata, aby UI dokazalo rict "tento insight vznikl z importu X v case Y".

**Partial result:** jestli AI umi streamovat nebo skladat vystup po castech, modeluj to jako `Status = Running/Completed/Failed` na analysis runu. Detail endpoint pro hotovy analysis nema skladat polovinu z cache a polovinu z workeru.

**PII redaction:** AI output muze obsahovat PII ze vstupu. Bud ho oznac jako `[PersonalData]`/encrypted podle UC75, nebo zajisti, ze CRM `IErasePersonalData` analysis smaze/anonymizuje. Neposilej raw prompt/output do klienta, pokud obsahuje interní data nebo tajemstvi.

**Frontend cache:** detail invaliduj po `marketing.pull_completed`/CRM realtime eventu, po retry a po GDPR erasure. Stary cached insight muze jinak ukazat PII nebo zastarale doporuceni.

**Testy k CRM:** owner detail 200, foreign id 404, missing id 404, detail obsahuje `InsightsJson`, list detail JSON neobsahuje, erased user data se nevrati.

**Nepouzijes:** AI call v GET detailu, 403 pro foreign id, raw exception/prompt text, partial response bez statusu, ani dlouhodobou frontend cache bez invalidace.

**EC:**

- EC416 foreign analysis -> 404 → filtruj `Id && UserId`, aby nebyla enumerace cizich analysis ids.
- EC417 not found → `NotFoundException("crm.analysis_not_found", ...)`, ne prazdny objekt.
- EC418 partial result → rozlis hotovy analysis detail od running analysis runu; GET detail nema vracet napul hotovy JSON bez statusu.
- EC419 PII redaction → AI output s PII musi byt encrypted/anonymizovatelny nebo mazany v CRM eraseru.
- EC420 stale cache → invaliduj detail po novem insightu, retry, import refreshi a GDPR erasure.

### UC85 Start vibe conversation

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /marketing/vibe/conversations`.

**Co se stane:** Marketing zalozi AI conversation.

**Napises v CRM:** pokud delas CRM assistant, vytvoris `CrmConversation`.

**EC:**

- EC421 title/prompt validation.
- EC422 user quota/credits.
- EC423 duplicate start.
- EC424 owner scope.
- EC425 initial system prompt version.

### UC86 Send vibe message async

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /marketing/vibe/conversations/{id}/messages`.

**Co se stane:** Message se ulozi a worker vygeneruje odpoved.

**Napises v CRM:** message command + worker command.

**EC:**

- EC426 conversation not found.
- EC427 foreign conversation.
- EC428 AI provider down.
- EC429 duplicate message retry.
- EC430 response status pending/failed.

### UC87 Stream vibe message

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /marketing/vibe/conversations/{id}/messages/stream`.

**Co se stane:** UX muze streamovat odpoved, ale stav se stale musi ulozit.

**Napises v CRM:** streaming jen pokud opravdu zlepsi UX.

**EC:**

- EC431 stream disconnect.
- EC432 response partial.
- EC433 provider timeout.
- EC434 persistence after stream failure.
- EC435 no business truth only in stream.

### UC88 List conversations

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/vibe/conversations`.

**Co se stane:** User vidi svoje conversations.

**Napises v CRM:** list CRM assistant sessions.

**EC:**

- EC436 paging.
- EC437 owner scope.
- EC438 archived/deleted state.
- EC439 sort by recent activity.
- EC440 empty state.

### UC89 Get conversation

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/vibe/conversations/{conversationId}`.

**Co se stane:** Marketing vrati messages a metadata.

**Napises v CRM:** detail conversation view.

**EC:**

- EC441 foreign id.
- EC442 not found.
- EC443 large message history.
- EC444 erased PII.
- EC445 stale pending message.

### UC90 Delete conversation

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `DELETE /marketing/vibe/conversations/{conversationId}`.

**Co se stane:** Conversation se smaze nebo soft-delete podle modulu.

**Napises v CRM:** delete mutation a invalidace list/detail.

**EC:**

- EC446 foreign id.
- EC447 already deleted.
- EC448 pending worker message.
- EC449 stale list.
- EC450 GDPR erase overlap.

### UC91 Marketing GDPR

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Marketing `IExportPersonalData` a `IErasePersonalData`.

**Co se stane:** Marketing data jsou v GDPR exportu/erase.

**Napises v CRM:** stejne porty pro CRM PII.

**EC:**

- EC451 exporter missing.
- EC452 eraser missing.
- EC453 external provider data vs local data.
- EC454 delete vs anonymize.
- EC455 tests pro export/erase.

## Cross-module komunikace

### UC92 Precti data z ciziho modulu

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** public query contract.

**Co se stane:** CRM zavola `dispatcher.Query(...)`, cilovy modul vrati DTO.

**Napises v CRM:** zavolani query, zadny cizi DbContext.

**EC:**

- EC456 query contract chybi -> nejdriv ho navrhni v owner modulu.
- EC457 query nesmi mutovat.
- EC458 DTO nesmi byt Core entity.
- EC459 performance pro casty list.
- EC460 permission/tenant scope musi resit owner modulu.

### UC93 Spust cizi akci hned

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** public command contract.

**Co se stane:** CRM zavola `dispatcher.Send(...)` a ceka vysledek.

**Napises v CRM:** command orchestration, ne copy-paste business logiky.

**EC:**

- EC461 command musi byt vlastneny cilovym modulem.
- EC462 idempotency pro penize/notifikace.
- EC463 validation v cilovem modulu.
- EC464 transakce pres vice modulu neni automaticka.
- EC465 dlouha prace nepatri do sync commandu.

### UC94 Oznám fakt ostatnim

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** integration event v `*.Contracts`.

**Co se stane:** Modul ulozi vlastni stav a event atomicky pres outbox.

**Napises v CRM:** event typu `DealCreatedIntegrationEvent`, pokud ostatni moduly maji reagovat.

**EC:**

- EC466 event je past-tense fact.
- EC467 event payload minimalni, hlavne IDs.
- EC468 publish az po DB state.
- EC469 event handler order neni garantovany.
- EC470 event neni request-response.

### UC95 Reaguj na event

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** public Wolverine handler + internal command.

**Co se stane:** Worker spusti handler, handler dispatchne command.

**Napises v CRM:** `public sealed class SomethingHappenedHandler`.

**EC:**

- EC471 handler musi byt public.
- EC472 handler musi byt v `ConfigureMessaging`.
- EC473 business logika patri do internal commandu.
- EC474 retry spusti handler znovu.
- EC475 idempotency key nebo unique constraint.

### UC96 Lokalni projekce cizich dat

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** event handler + CRM read table.

**Co se stane:** CRM si drzi malou kopii dat pro rychly list.

**Napises v CRM:** `CrmUserSnapshot` nebo `CrmBillingSnapshot`.

**EC:**

- EC476 projection stale.
- EC477 event lost/replay gap -> reconcile.
- EC478 minimalni PII.
- EC479 schema version.
- EC480 source module zustava owner.

### UC97 Kratky request-response pres bus

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `IMessageBus.InvokeAsync<T>`.

**Co se stane:** Pro kratkou worker praci muzes ziskat response.

**Napises v CRM:** jen kdyz prace neni dlouha a nepotrebuje status page.

**EC:**

- EC481 timeout.
- EC482 dlouha prace musi jit do Operations.
- EC483 idempotency.
- EC484 retry semantics.
- EC485 user feedback pri timeoutu.

### UC98 Vice subscriberu na jeden event

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Wolverine durable handlers.

**Co se stane:** Jeden event muze mit vice handleru.

**Napises v CRM:** kazdy handler order-independent a idempotentni.

**EC:**

- EC486 retry muze zopakovat vice handleru.
- EC487 handler A nesmi spolehat na handler B.
- EC488 dead-letter blokuje side-effect podle behavior.
- EC489 no global ordering.
- EC490 commutative state changes preferovat.

### UC99 Event s minimem PII

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `IIntegrationEvent` DTO.

**Co se stane:** Event prenese jen fakta, ktera consumer potrebuje.

**Napises v CRM:** `record DealWonIntegrationEvent(Guid DealId, Guid UserId, ...)`.

**EC:**

- EC491 email/body v durable envelope muze zit dele nez chces.
- EC492 payload se neda crypto-shreddnout stejne jako DB.
- EC493 consumer si PII nacte pres povoleny contract.
- EC494 event wire name musi zustat kompatibilni.
- EC495 event verze pri breaking change.

### UC100 Module jobs

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `IModule.RegisterJobs`.

**Co se stane:** Modul registruje Quartz job.

**Napises v CRM:** job class, ktera jen dispatchne command.

**EC:**

- EC496 cron UTC.
- EC497 job DI graph musi bootnout v Jobs hostu.
- EC498 command idempotentni.
- EC499 cap per run.
- EC500 no HTTP-only dependency in job.

### UC101 Novy modul

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** trio `Core`, `Contracts`, `Tests`.

**Co se stane:** Modul se registruje do Api, Worker, Jobs, Migration hostu a architecture tests.

**Napises v CRM:** `CrmModule`, `CrmDbContext`, vertical slices, contracts.

**EC:**

- EC501 Core internal.
- EC502 Contracts nesmi referencovat Core.
- EC503 host registration ve vsech hostech.
- EC504 migrations pres admin connection.
- EC505 architecture tests pro boundary.

## Frontend a platform building blocks

### UC102 Frontend route

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Next route v `frontend/app`.

**Co se stane:** Route slozi page layout a feature komponenty.

**Napises v CRM:** route bez business fetch logiky.

**EC:**

- EC506 route nesmi duplikovat API logiku.
- EC507 loading/error/empty states.
- EC508 permission/entitlement UI guard.
- EC509 responsive layout.
- EC510 route-level redirect jen pro auth UX, backend zustava autorita.

### UC103 Frontend API file

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `features/crm/api.ts`.

**Co se stane:** Typy request/response a `apiFetch` calls jsou na jednom miste.

**Napises v CRM:** queryOptions a mutation functions.

**EC:**

- EC511 endpoint path bez `/v1`, pokud BFF/apiFetch prefixuje.
- EC512 response type drift.
- EC513 404 empty state vs real error.
- EC514 abort/cancel pro long fetch.
- EC515 no fetch directly in component.

### UC104 Frontend hooks

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `features/crm/hooks.ts`.

**Co se stane:** UI pouziva `useQuery`, `useMutation`, invalidace a toast.

**Napises v CRM:** hook pro kazdou mutaci.

**EC:**

- EC516 double click.
- EC517 stale list/detail.
- EC518 optimistic rollback.
- EC519 toast pred navigation.
- EC520 disable pending controls.

### UC105 Form validation

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Zod + react-hook-form.

**Co se stane:** Frontend validuje pro UX, backend je autorita.

**Napises v CRM:** schema mirrorujici backend validator.

**EC:**

- EC521 frontend schema drift.
- EC522 backend validation error mapping.
- EC523 empty/trim normalization.
- EC524 locale/i18n messages.
- EC525 accessibility error focus.

### UC106 Cache invalidace po mutaci

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** React Query `invalidateQueries`.

**Co se stane:** Po create/update/delete se UI refetchne.

**Napises v CRM:** invalidace listu, detailu, countu a souvisejicich roots.

**EC:**

- EC526 delete vypada nefunkcne kvuli stale cache.
- EC527 create se nezobrazi kvuli sort/order.
- EC528 related Billing/Notifications cache.
- EC529 realtime plus mutation double-refetch.
- EC530 stale detail po rename/delete.

### UC107 Realtime frontend refresh

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** SSE event map.

**Co se stane:** Event invaliduje query keys.

**Napises v CRM:** mapping `crm.*` eventu.

**EC:**

- EC531 SSE reconnect.
- EC532 lost event.
- EC533 event pred DB commit.
- EC534 duplicate event.
- EC535 query refetch je source of truth.

### UC108 Navigation guard

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** entitlements + permissions.

**Co se stane:** UI skryje moduly a akce, ktere user nema.

**Napises v CRM:** nav item se cte z auth/tenant state.

**EC:**

- EC536 UI guard neni security.
- EC537 stale token permissions.
- EC538 entitlement disabled while user is on page.
- EC539 deep link directly to route.
- EC540 loading state pri nacitani entitlements.

### UC109 Error UX

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `apiFetch` + RFC9457 ProblemDetails.

**Co se stane:** Frontend mapuje error code na text a stav.

**Napises v CRM:** error handling pro form/list/detail.

**EC:**

- EC541 401 redirect login.
- EC542 403 no permission.
- EC543 404 empty/not found.
- EC544 422 validation/business.
- EC545 429 retry-after.

### UC110 i18n error codes

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `SharedResource.resx` a `SharedResource.cs.resx`.

**Co se stane:** Backend error code ma EN/CZ text.

**Napises v CRM:** nove error codes do obou resx.

**EC:**

- EC546 missing resx key.
- EC547 client hardcoded message.
- EC548 wrong locale fallback.
- EC549 validation error code mismatch.
- EC550 tests/smoke pro lokalizaci.

### UC111 Migrace modulu

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** EF migrations + `MigrationService`.

**Co se stane:** Modulove migrace bezi pres admin connection a RLS bootstrap.

**Napises v CRM:** migration, design-time factory, host registration.

**EC:**

- EC551 nespoustet migraci proti shared DB.
- EC552 runtime RLS role nema delat DDL.
- EC553 missing design-time factory.
- EC554 migration service DI graph fail.
- EC555 raw SQL zakazany.

### UC112 Testy noveho flow

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `tests/ModularPlatform.IntegrationTesting` a architecture tests.

**Co se stane:** Testy bootuji realne hosty a overuji boundaries.

**Napises v CRM:** slice/integration tests, boundary tests podle potreby.

**EC:**

- EC556 nepsat druhy Testcontainers fixture.
- EC557 host registration chybi -> HostBootTests fail.
- EC558 contracts/core boundary violation.
- EC559 event wire name drift.
- EC560 test pokryje happy path i edge path.
