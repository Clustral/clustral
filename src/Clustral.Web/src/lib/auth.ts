import NextAuth from "next-auth";

export const { handlers, signIn, signOut, auth } = NextAuth({
  providers: [
    {
      id: "oidc",
      name: "SSO",
      type: "oidc",
      issuer: process.env.OIDC_ISSUER,
      clientId: process.env.OIDC_CLIENT_ID || "clustral-web",
      clientSecret: process.env.OIDC_CLIENT_SECRET || "",
    },
  ],
  callbacks: {
    async jwt({ token, account }) {
      // On initial sign-in, persist the access token from the OIDC provider.
      if (account) {
        token.accessToken = account.access_token;
      }
      return token;
    },
    async session({ session, token }) {
      // Expose the access token to the client via the session.
      (session as any).accessToken = token.accessToken;
      return session;
    },
  },
  pages: {
    signIn: "/login",
  },
  trustHost: true,
});
