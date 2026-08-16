# BzsOIDC Identity Context

This context names the identity platform concepts shared by administration, authorization, and OIDC flows.

## Permission Topology

**Permission topology**:
The connected view of protected resources, permissions, release scopes, roles, and permission assignments.
_Avoid_: permission catalog when the relationship between these concepts matters.

**Protected resource**:
An addressable capability area to which permissions belong.
_Avoid_: API when referring to the authorization concept rather than a transport.

**Permission**:
An individually assignable authorization capability within a protected resource.
_Avoid_: scope; a scope is a release condition, not an assignment.

**Release scope**:
A scope that permits a permission claim to be released for a particular OIDC request.
_Avoid_: permission; release scope controls claim release rather than role assignment.

**Role**:
A named collection of permissions that can be granted to a user.
_Avoid_: group when referring to authorization.

**Permission assignment**:
The relationship that grants a permission to a role.
_Avoid_: claim when referring to the domain relationship rather than its Identity representation.

## OIDC Administration

**OIDC client**:
An application registered to request identity or access tokens from the identity platform.
_Avoid_: consumer when referring to the registered application.

**OIDC client profile**:
The configuration that describes an OIDC client's authentication flow, consent, redirect URIs, proof key requirements, grants, and requested release scopes.
_Avoid_: OpenIddict descriptor when referring to the domain configuration rather than its infrastructure representation.

**Authorization consent**:
The decision that a user has authorized an OIDC client to act for that user under a particular set of release scopes.
_Avoid_: authorization when the user's consent decision, rather than the resulting protocol record, is what matters.

**User administration**:
The workflow through which an administrator creates, changes, or removes a user and coordinates that user's password and Role membership.
_Avoid_: user management when referring only to low-level Identity operations.

**OIDC administration topology**:
The administrative view of relationships among OIDC clients, release scopes, and permission topology.
_Avoid_: permission topology when OIDC client relationships are also included.
