import React, { createContext, useContext, useEffect, useState } from "react";
import {
  User,
  signInWithEmailAndPassword,
  signOut as fbSignOut,
  onAuthStateChanged,
  sendPasswordResetEmail,
} from "firebase/auth";
import { doc, getDoc, getDocs, collection } from "firebase/firestore";
import { auth, db } from "../services/firebase";

interface AuthContextType {
  user: User | null;
  isAdmin: boolean;
  isSuperAdmin: boolean;
  loading: boolean;
  error: string | null;
  login: (email: string, pass: string) => Promise<void>;
  logout: () => Promise<void>;
  resetPassword: (email: string) => Promise<void>;
  clearError: () => void;
  recheckPermissions: () => Promise<boolean>;
}

const AuthContext = createContext<AuthContextType>({} as AuthContextType);

const MASTER_ADMIN_EMAILS = [
  "nexuslsatulizador@gmail.com",
  "jarbasantonio201@gmail.com"
];

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [isAdmin, setIsAdmin] = useState(false);
  const [isSuperAdmin, setIsSuperAdmin] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const checkUserAdminStatus = async (currentUser: User): Promise<boolean> => {
    const email = (currentUser.email || "").toLowerCase().trim();

    // 1. Verificação prioritária de email mestre
    if (MASTER_ADMIN_EMAILS.includes(email)) {
      setIsAdmin(true);
      setIsSuperAdmin(true);
      return true;
    }

    // 2. Verifica Custom Claim no Token JWT
    try {
      const tokenResult = await currentUser.getIdTokenResult(true);
      if (tokenResult.claims.admin === true) {
        setIsAdmin(true);
        setIsSuperAdmin(tokenResult.claims.superadmin === true || true);
        return true;
      }
    } catch { }

    // 3. Fallback: Coleção 'admins' no Firestore
    try {
      if (email) {
        const adminDoc = await getDoc(doc(db, "admins", email));
        if (adminDoc.exists() && adminDoc.data()?.ativo === true) {
          setIsAdmin(true);
          setIsSuperAdmin(adminDoc.data()?.role === "superadmin");
          return true;
        }

        // Caso especial: primeiro acesso se a coleção admins estiver vazia
        const allAdmins = await getDocs(collection(db, "admins"));
        if (allAdmins.empty) {
          setIsAdmin(true);
          setIsSuperAdmin(true);
          return true;
        }
      }
    } catch (err) {
      console.warn("Aviso na consulta da coleção admins:", err);
    }

    setIsAdmin(false);
    setIsSuperAdmin(false);
    return false;
  };

  useEffect(() => {
    const unsubscribe = onAuthStateChanged(auth, async (currentUser) => {
      setLoading(true);
      if (currentUser) {
        const allowed = await checkUserAdminStatus(currentUser);
        if (allowed) {
          setUser(currentUser);
          setError(null);
        } else {
          await fbSignOut(auth);
          setUser(null);
          setIsAdmin(false);
          setIsSuperAdmin(false);
          setError("Acesso não autorizado: Esta conta não possui privilégios de administrador.");
        }
      } else {
        setUser(null);
        setIsAdmin(false);
        setIsSuperAdmin(false);
      }
      setLoading(false);
    });

    return () => unsubscribe();
  }, []);

  const login = async (email: string, pass: string) => {
    setError(null);
    setLoading(true);
    try {
      const cred = await signInWithEmailAndPassword(auth, email.trim(), pass);
      const allowed = await checkUserAdminStatus(cred.user);
      if (!allowed) {
        await fbSignOut(auth);
        setUser(null);
        throw new Error("Acesso negado: Seu usuário não possui privilégios administrativos no Nexus LS.");
      }
      setUser(cred.user);
    } catch (err: any) {
      console.error("Erro no login:", err);
      let msg = "Falha na autenticação. Verifique suas credenciais.";
      if (err.code === "auth/invalid-credential" || err.code === "auth/user-not-found" || err.code === "auth/wrong-password") {
        msg = "E-mail ou senha incorretos.";
      } else if (err.code === "auth/too-many-requests") {
        msg = "Muitas tentativas sem sucesso. Aguarde alguns minutos.";
      } else if (err.message) {
        msg = err.message;
      }
      setError(msg);
      throw new Error(msg);
    } finally {
      setLoading(false);
    }
  };

  const logout = async () => {
    try {
      await fbSignOut(auth);
      setUser(null);
      setIsAdmin(false);
      setIsSuperAdmin(false);
      setError(null);
    } catch (err) {
      console.error("Erro ao sair:", err);
    }
  };

  const resetPassword = async (email: string) => {
    setError(null);
    try {
      await sendPasswordResetEmail(auth, email.trim());
    } catch (err: any) {
      console.error("Erro ao redefinir senha:", err);
      throw new Error("Não foi possível enviar o e-mail de redefinição de senha.");
    }
  };

  const recheckPermissions = async (): Promise<boolean> => {
    if (!auth.currentUser) return false;
    return await checkUserAdminStatus(auth.currentUser);
  };

  const clearError = () => setError(null);

  return (
    <AuthContext.Provider
      value={{
        user,
        isAdmin,
        isSuperAdmin,
        loading,
        error,
        login,
        logout,
        resetPassword,
        clearError,
        recheckPermissions,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => useContext(AuthContext);
