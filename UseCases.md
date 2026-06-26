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

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /billing/packages`.

**Co se stane:** Billing vrati koupitelne credit packages.

**Napises v CRM:** nic.

**EC:**

- EC161 disabled package se neprodava.
- EC162 stabilni sort order.
- EC163 cena a kredit jsou Billing source of truth.
- EC164 stale cache po admin edit.
- EC165 CRM nehardcoduje package ids.

### UC34 Admin package list

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /billing/admin/packages`.

**Co se stane:** Admin vidi vsechny packages vcetne disabled.

**Napises v CRM:** nic.

**EC:**

- EC166 admin permission.
- EC167 include disabled.
- EC168 paging/order.
- EC169 audit admin change.
- EC170 oddelit admin list a public list.

### UC35 Create package

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /billing/admin/packages`.

**Co se stane:** Admin vytvori novy package.

**Napises v CRM:** nic.

**EC:**

- EC171 amount/price validation.
- EC172 currency validation.
- EC173 duplicate key/name.
- EC174 disabled/default stav podle business rozhodnuti.
- EC175 po create invalidovat admin i public list.

### UC36 Update package

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `PUT /billing/admin/packages/{packageId}`.

**Co se stane:** Admin zmeni package.

**Napises v CRM:** nic.

**EC:**

- EC176 not found.
- EC177 zmena ceny nesmi prepsat historicke purchases.
- EC178 disabled package zmizi z public listu.
- EC179 concurrent update.
- EC180 audit change.

### UC37 Purchase package checkout

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /billing/packages/{packageId}/checkout`.

**Co se stane:** Billing zalozi purchase sagou a checkout session.

**Napises v CRM:** redirect do checkoutu.

**EC:**

- EC181 package disabled/not found.
- EC182 duplicate checkout click.
- EC183 provider unavailable.
- EC184 saga timeout.
- EC185 kredit se grantuje az po confirmed webhooku.

### UC38 Purchase status

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /billing/purchases/{purchaseId}`.

**Co se stane:** Frontend polluje purchase/saga stav.

**Napises v CRM:** nic.

**EC:**

- EC186 foreign purchase -> 404.
- EC187 pending/confirmed/abandoned states.
- EC188 late webhook after timeout.
- EC189 frontend polling interval a loading.
- EC190 CRM necita Billing DB.

### UC39 Subscription plans

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /billing/subscriptions/plans`.

**Co se stane:** Billing vrati config-driven plany.

**Napises v CRM:** jen mapovani plan -> CRM capability, pokud produkt potrebuje.

**EC:**

- EC191 config missing/invalid.
- EC192 disabled plan.
- EC193 stale frontend cache.
- EC194 plan key musi byt stabilni.
- EC195 CRM neparsuje raw Billing config.

### UC40 Subscription checkout

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /billing/subscriptions/checkout`.

**Co se stane:** Billing vytvori subscription checkout.

**Napises v CRM:** redirect a refetch po navratu.

**EC:**

- EC196 existing active subscription.
- EC197 checkout session expired.
- EC198 webhook out-of-order.
- EC199 double checkout okno pred webhookem.
- EC200 UI disable pending state.

### UC41 My subscription

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /billing/subscriptions/me`.

**Co se stane:** Billing vrati aktualni subscription stav.

**Napises v CRM:** ctes jen pro UI nebo entitlement rozhodnuti pres policy.

**EC:**

- EC201 no subscription -> empty state.
- EC202 past_due state.
- EC203 cancel at period end.
- EC204 stale webhook state resi reconcile.
- EC205 no CRM-local subscription copy.

### UC42 Cancel subscription

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /billing/subscriptions/cancel`.

**Co se stane:** Billing zavola provider a zmeni stav subscription.

**Napises v CRM:** invalidace subscription a entitlement UI.

**EC:**

- EC206 no active subscription.
- EC207 provider failure.
- EC208 idempotent cancel.
- EC209 entitlement revocation timing.
- EC210 invalidovat subscription i entitlements.

### UC43 Billing portal

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /billing/portal`.

**Co se stane:** Billing vytvori provider portal session.

**Napises v CRM:** redirect.

**EC:**

- EC211 missing customer id.
- EC212 provider down.
- EC213 return URL validation.
- EC214 portal session expired.
- EC215 CRM nevytvari portal session samo.

### UC44 Promo code

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /billing/promo-codes/{code}/validate`.

**Co se stane:** Billing overi promo code.

**Napises v CRM:** nic, jen UI input/debounce.

**EC:**

- EC216 invalid/expired code.
- EC217 code not applicable.
- EC218 provider rate limit.
- EC219 frontend debounce.
- EC220 checkout stejne validuje na backendu.

### UC45 Stripe reconcile

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `BillingStripeReconcileJob`.

**Co se stane:** Jobs host requeue stuck Stripe events a opravuje subscription drift.

**Napises v CRM:** vlastni reconcile pro CRM externi systemy.

**EC:**

- EC221 stuck `stripe_events`.
- EC222 subscription drift.
- EC223 provider API down.
- EC224 cap per run.
- EC225 warning metrics/logs.

### UC46 Expirace kreditu

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `BillingExpireCreditsJob`.

**Co se stane:** Jobs host materializuje expirace bucketu/holdu.

**Napises v CRM:** nic.

**EC:**

- EC226 expired buckets.
- EC227 hold overlapping bucket.
- EC228 idempotency key per expiration.
- EC229 UTC cron.
- EC230 CRM nesmi upravovat bucket remaining.

### UC47 Provision credit account

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Billing handler na `UserRegisteredIntegrationEvent`.

**Co se stane:** Po registraci vznikne zero-balance credit account.

**Napises v CRM:** kopiruj pattern pro svoje onboarding projekce.

**EC:**

- EC231 duplicate event.
- EC232 worker retry.
- EC233 account already exists.
- EC234 multiple handlers on same event.
- EC235 public handler dispatchuje internal command.

## Notifications

### UC48 Poslat notifikaci

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /notifications/send` nebo `SendNotificationCommand`.

**Co se stane:** Notifications ulozi in-app notifikaci a/nebo zaradi email/push delivery.

**Napises v CRM:** command/event s recipientem, template key, daty a idempotency key.

**EC:**

- EC236 missing template.
- EC237 invalid channel.
- EC238 duplicate send retry.
- EC239 recipient z tokenu nebo validni target.
- EC240 publish realtime az po commitu.

### UC49 Zobrazit moje notifikace

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /notifications/me`.

**Co se stane:** User vidi owner-scoped feed.

**Napises v CRM:** nic.

**EC:**

- EC241 paging.
- EC242 unread filter.
- EC243 foreign notification hidden.
- EC244 erased PII v title/body.
- EC245 stale cache po mark-read.

### UC50 Unread count

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /notifications/me/unread-count`.

**Co se stane:** Frontend vidi pocet neprectenych.

**Napises v CRM:** jen badge v navigaci.

**EC:**

- EC246 count stale po mark-read.
- EC247 SSE event lost -> refetch fallback.
- EC248 count owner-scoped.
- EC249 loading skeleton.
- EC250 neudrzovat druhy unread counter v CRM.

### UC51 Mark notification read

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /notifications/{notificationId}/read`.

**Co se stane:** Notifications oznaci jednu notifikaci jako prectenou.

**Napises v CRM:** mutation invaliduje list a unread count.

**EC:**

- EC251 foreign id -> 404.
- EC252 already read -> idempotent/no-op.
- EC253 double click.
- EC254 stale feed.
- EC255 optimistic update rollback pri erroru.

### UC52 Mark all read

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /notifications/me/read-all`.

**Co se stane:** Notifications oznaci vsechny moje notifikace jako prectene.

**Napises v CRM:** button disable behem pending.

**EC:**

- EC256 empty feed.
- EC257 concurrent new notification.
- EC258 stale unread count.
- EC259 bulk update audit caveat podle implementace.
- EC260 retry nesmi rozbit stav.

### UC53 Welcome notification

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `SendWelcomeHandler` na `UserRegisteredIntegrationEvent`.

**Co se stane:** Worker posle welcome notifikaci po registraci.

**Napises v CRM:** podobny handler pro CRM onboarding jen pokud je potreba.

**EC:**

- EC261 missing welcome template.
- EC262 duplicate event.
- EC263 handler retry.
- EC264 user erased before delivery.
- EC265 handler musi byt explicitne registrovany.

### UC54 Purchase completed notification

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Notifications handler na `CreditPurchaseCompletedIntegrationEvent`.

**Co se stane:** Po nakupu kreditu dostane user notifikaci.

**Napises v CRM:** pro `DealWonIntegrationEvent` nebo `AiRunCompletedEvent` stejny pattern.

**EC:**

- EC266 Billing event duplicate.
- EC267 notifikace musi mit idempotency key.
- EC268 purchase event order.
- EC269 missing template.
- EC270 retry nesmi poslat dva emaily.

### UC55 Email delivery

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `EmailDeliveryHandler`.

**Co se stane:** Worker doruci email pres `IEmailSender`.

**Napises v CRM:** nic, CRM jen vyvola notification.

**EC:**

- EC271 SMTP down.
- EC272 transient retry.
- EC273 permanent bounce.
- EC274 PII v email body.
- EC275 DLQ monitoring.

### UC56 Push delivery

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `PushDeliveryHandler`.

**Co se stane:** Worker doruci push pres `IPushSender`.

**Napises v CRM:** nic.

**EC:**

- EC276 push provider no-op/dev.
- EC277 missing device token.
- EC278 provider failure.
- EC279 duplicate push retry.
- EC280 user revoked notifications consent.

## Files

### UC57 Upload souboru

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /files`.

**Co se stane:** Files ulozi blob pres `IFileStorage` a metadata do `file_objects`.

**Napises v CRM:** po uploadu ulozis `FileObjectId` k dealu/kontaktu.

**EC:**

- EC281 file > 10 MB.
- EC282 disallowed content type.
- EC283 client filename nesmi byt storage key.
- EC284 blob upload uspeje, metadata failne -> cleanup/reconcile gap.
- EC285 frontend validace je jen UX, backend je autorita.

### UC58 List moje soubory

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /files`.

**Co se stane:** Files vrati owner-scoped paged list.

**Napises v CRM:** vlastni list attachmentu pres CRM join entity.

**EC:**

- EC286 paging.
- EC287 search/filter stale.
- EC288 owner scope.
- EC289 deleted files.
- EC290 CRM nema listovat cizi user files.

### UC59 Download souboru

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /files/{fileId}`.

**Co se stane:** Files overi ownership a streamuje blob.

**Napises v CRM:** link/button s `FileObjectId`.

**EC:**

- EC291 foreign file id -> 404.
- EC292 blob missing.
- EC293 content type.
- EC294 large stream failure.
- EC295 storage key traversal guard.

### UC60 Rename souboru

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `PATCH /files/{fileId}`.

**Co se stane:** Files zmeni display filename metadata.

**Napises v CRM:** po rename invaliduj attachments/list.

**EC:**

- EC296 empty filename.
- EC297 too long filename.
- EC298 foreign id.
- EC299 concurrent rename.
- EC300 CRM nema prepisovat metadata napriamo.

### UC61 Delete souboru

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `DELETE /files/{fileId}`.

**Co se stane:** Files smaze metadata a blob podle implementace.

**Napises v CRM:** odstranis/deaktivujes vazbu `DealAttachment`.

**EC:**

- EC301 foreign id -> 404.
- EC302 already deleted.
- EC303 blob delete fails.
- EC304 CRM vazba zustane orphan -> cleanup.
- EC305 UI stale list.

### UC62 Pripojit file k CRM entite

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Files upload + CRM join table.

**Co se stane:** CRM ma `DealAttachment(DealId, FileObjectId, UserId)`.

**Napises v CRM:** command `AttachFileToDealCommand`.

**EC:**

- EC306 deal foreign user -> 404.
- EC307 file foreign user -> 404.
- EC308 duplicate attachment.
- EC309 deal deleted.
- EC310 GDPR erase musi odstranit i vazby nebo anonymizovat metadata.

## Operations, Worker, Jobs, Realtime

### UC63 Start dlouhe operace

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Operations pattern `POST /operations/demo` nebo vlastni CRM run table.

**Co se stane:** HTTP request ulozi stav a outbox message, vrati 202/status id.

**Napises v CRM:** `StartCrmImportCommand` nebo `StartCrmAiRunCommand`.

**EC:**

- EC311 request nesmi cekat na dlouhou praci.
- EC312 operation musi byt owner-scoped.
- EC313 duplicate click.
- EC314 outbox publish a DB save atomicky.
- EC315 response musi obsahovat status link/id.

### UC64 Get operation status

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /operations/{operationId}` nebo CRM status endpoint.

**Co se stane:** Frontend polluje stav.

**Napises v CRM:** status DTO s `Pending/Processing/Completed/Failed`.

**EC:**

- EC316 foreign operation -> 404.
- EC317 operation not found.
- EC318 stuck processing.
- EC319 terminal state guard.
- EC320 frontend polling backoff.

### UC65 List moje operace

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /operations`.

**Co se stane:** User vidi historii svych dlouhych tasku.

**Napises v CRM:** pokud chces domenu detailneji, vlastni list `CrmRuns`.

**EC:**

- EC321 paging.
- EC322 old operations retention.
- EC323 owner scope.
- EC324 sort order.
- EC325 empty state.

### UC66 Worker dokonci praci

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** Wolverine handler.

**Co se stane:** Worker zpracuje message a prevede stav do terminal state.

**Napises v CRM:** public handler + internal command, idempotentni.

**EC:**

- EC326 message retry.
- EC327 duplicate message.
- EC328 worker crash between external call and save.
- EC329 terminal state no-op.
- EC330 DLQ needs support/reconcile story.

### UC67 Cron job

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `IModule.RegisterJobs` + Quartz.

**Co se stane:** Jobs host spousti command podle UTC cronu.

**Napises v CRM:** `CrmReconcileJob` jen dispatchne `ReconcileCrmCommand`.

**EC:**

- EC331 cron in UTC.
- EC332 job nesmi obsahovat business logiku.
- EC333 multi-instance scheduling.
- EC334 retry/idempotency.
- EC335 cap per run.

### UC68 Messaging health

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `MessagingHealthJob`.

**Co se stane:** Platform sleduje pending outgoing/incoming/dead letters.

**Napises v CRM:** vlastni domenu reconcile, pokud stuck run potrebuje opravu.

**EC:**

- EC336 DLQ neni business recovery.
- EC337 alert bez akce nestaci.
- EC338 stuck threshold.
- EC339 no raw SQL do Wolverine tables.
- EC340 metrics musi mit jasny owner.

### UC69 Realtime stream

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /realtime/stream`.

**Co se stane:** Browser dostava SSE eventy a replay podle `Last-Event-ID`.

**Napises v CRM:** event type mapping na invalidaci `queryRoots.crm`.

**EC:**

- EC341 SSE disconnect.
- EC342 replay je best-effort, ne durable truth.
- EC343 Redis fallback/local mode rozdily.
- EC344 event pred commitem nesmi odejit.
- EC345 frontend ma refetch fallback.

## GDPR, PII, Audit

### UC70 Export osobnich dat

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /gdpr/me/export`.

**Co se stane:** GDPR modul zavola vsechny `IExportPersonalData` implementace.

**Napises v CRM:** `CrmPersonalDataExporter` a registraci v `CrmModule`.

**EC:**

- EC346 exporter chybi -> CRM data nejsou v exportu.
- EC347 exporter throw nesmi shodit cely export.
- EC348 export foreign user zakazan.
- EC349 PII z encrypted columns se cte pres converter.
- EC350 export format musi byt stabilni.

### UC71 Request erasure

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /gdpr/me/erase`.

**Co se stane:** GDPR publikuje erasure event a erasers modulu anonymizuji/smazou PII.

**Napises v CRM:** `CrmPersonalDataEraser`.

**EC:**

- EC351 eraser chybi -> CRM PII prezije.
- EC352 jeden eraser failure nesmi blokovat crypto-shred vseho navzdy.
- EC353 ledger/audit se nema fyzicky mazat, ale anonymizovat.
- EC354 erasure retry idempotentni.
- EC355 po erasure login/refresh nesmi fungovat.

### UC72 Grant consent

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /gdpr/consents/grant`.

**Co se stane:** GDPR ulozi consent record.

**Napises v CRM:** ctes consent stav, nevytvaris paralelni consent table.

**EC:**

- EC356 duplicate grant.
- EC357 unknown consent key.
- EC358 consent musi byt audit/export.
- EC359 frontend stale consent state.
- EC360 legal text/version.

### UC73 Withdraw consent

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /gdpr/consents/withdraw`.

**Co se stane:** GDPR ulozi withdrawal.

**Napises v CRM:** prestanes delat akce vyzadujici consent.

**EC:**

- EC361 withdraw bez grant.
- EC362 stale frontend.
- EC363 background job musi znovu cist consent.
- EC364 audit/export.
- EC365 consent withdrawal neni delete vsech dat.

### UC74 Get consents

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /gdpr/me/consents`.

**Co se stane:** Frontend zobrazi consent centrum.

**Napises v CRM:** nic, jen guardy pro CRM features podle consentu.

**EC:**

- EC366 empty state.
- EC367 locale/legal text.
- EC368 stale query.
- EC369 owner scope.
- EC370 unsupported consent key.

### UC75 PII v CRM datech

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `[PersonalData]`, `[Encrypted]`, `IDataSubject`.

**Co se stane:** PII se encryptuje at rest a audit PII je crypto-shreddable.

**Napises v CRM:** entity atributy a blind index pro lookupy.

**EC:**

- EC371 encrypted column nejde hledat plaintextem.
- EC372 blind index key missing fail-fast.
- EC373 po shred se hodnota cte `[erased]`.
- EC374 atribut `[PersonalData]` bez `IDataSubject` je arch bug.
- EC375 neukladat PII do event payloadu zbytecne.

### UC76 Audit zmen

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `AuditInterceptor`.

**Co se stane:** Tracked `SaveChanges` zapise changed fields do audit table modulu.

**Napises v CRM:** nic extra, jen pouzivas tracked entities.

**EC:**

- EC376 `ExecuteUpdate` bypassuje audit.
- EC377 raw SQL bypassuje conventions.
- EC378 PII v auditu encryptovat.
- EC379 audit read permission.
- EC380 system context vs user context.

### UC77 Crypto-shred

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** GDPR subject key shred.

**Co se stane:** PII encrypted pod subject DEK uz nejde precist.

**Napises v CRM:** UI umi zobrazit erased/null stavy.

**EC:**

- EC381 tombstone se nesmi smazat.
- EC382 post-erasure write PII nesmi remintnout readable key.
- EC383 `[erased]` nesmi rozbit validators/UI.
- EC384 admin forensic read po shred vraci erased.
- EC385 cache s plaintext PII musi expirovat/refetchnout.

### UC78 Retention sweep

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GdprRetentionSweepJob`.

**Co se stane:** Platform ma seam pro retention, subject key tombstones zustavaji permanentne.

**Napises v CRM:** vlastni purge command pro purgeable CRM data, pokud existuji.

**EC:**

- EC386 tombstones se nemazou.
- EC387 cron UTC.
- EC388 purge jen module-owned data.
- EC389 legal retention vs user erase.
- EC390 idempotentni sweep.

## Marketing

### UC79 Spustit data pull

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `POST /marketing/pulls`.

**Co se stane:** Marketing ulozi pull a worker taha GA/GSC data pres gateway porty.

**Napises v CRM:** pro CRM import kopiruj pull/status/worker pattern.

**EC:**

- EC391 external API down.
- EC392 credentials missing.
- EC393 duplicate pull.
- EC394 rate limit.
- EC395 worker retry and status.

### UC80 Get pull status

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/pulls/{dataPullId}`.

**Co se stane:** Frontend polluje stav data pullu.

**Napises v CRM:** status endpoint pro vlastni import/AI run.

**EC:**

- EC396 foreign pull -> 404.
- EC397 not found.
- EC398 stuck processing.
- EC399 failed with reason.
- EC400 frontend polling backoff.

### UC81 List pulls

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/pulls`.

**Co se stane:** User vidi historii pulls.

**Napises v CRM:** list importu/runu.

**EC:**

- EC401 paging.
- EC402 sort order.
- EC403 owner scope.
- EC404 old failed items.
- EC405 empty state.

### UC82 List snapshots

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/snapshots`.

**Co se stane:** Marketing vrati ulozene read model snapshots.

**Napises v CRM:** pro casto ctena externi data ulozis snapshot/projekci.

**EC:**

- EC406 no snapshots.
- EC407 stale snapshot.
- EC408 paging.
- EC409 owner/tenant scope.
- EC410 schema version.

### UC83 List analyses

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/analyses`.

**Co se stane:** Marketing vrati AI analyses.

**Napises v CRM:** AI vystupy ukladas jako domenu, ne jen transient response.

**EC:**

- EC411 empty list.
- EC412 failed analysis.
- EC413 stale data source.
- EC414 paging.
- EC415 erased user data.

### UC84 Get analysis

**Status:** Backlog — implementovat a overit vcetne prirazenych EC.

**Pouzijes:** `GET /marketing/analyses/{analysisId}`.

**Co se stane:** Marketing vrati detail analysis.

**Napises v CRM:** detail endpoint pro CRM AI result.

**EC:**

- EC416 foreign analysis -> 404.
- EC417 not found.
- EC418 partial result.
- EC419 PII redaction.
- EC420 stale cache.

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
